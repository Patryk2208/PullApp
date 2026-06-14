# Trip Planner — Implementation Specification

> **DEPRECATED — superseded by the `feature/trip-planner/done-right` rebuild.**
> The current implementation diverges significantly from this document (different state machines, no Redis key schema, no polling workers). Refer to [`03-components/trip-planner.md`](../03-components/trip-planner.md) for accurate component documentation and [`04-flows/ride-lifecycle.md`](../04-flows/ride-lifecycle.md) for the as-built flows. This file is kept for historical reference only.

---

> **Audience:** This document is the single source of truth for implementing Trip Planner's business logic. It assumes the infrastructure layer (RabbitMQ, Redis, PostGIS, Kafka wiring) is already in place. Every flow, state, API contract, Redis key, and edge case is defined here. Do not infer behaviour that is not stated.

---

## Table of Contents

1. [Service Responsibilities](#1-service-responsibilities)
2. [Ride State Machine](#2-ride-state-machine)
3. [Data Model](#3-data-model)
4. [Redis Key Schema](#4-redis-key-schema)
5. [Kafka Events](#5-kafka-events)
6. [RabbitMQ Contracts](#6-rabbitmq-contracts)
7. [Flow 1 — Driver Registers a Route](#7-flow-1--driver-registers-a-route)
8. [Flow 2 — Driver Modifies or Cancels a Route](#8-flow-2--driver-modifies-or-cancels-a-route)
9. [Flow 3 — Driver Disconnects](#9-flow-3--driver-disconnects)
10. [Flow 4 — Passenger Requests a Match](#10-flow-4--passenger-requests-a-match)
11. [Flow 5 — Match Confirmation Timeout](#11-flow-5--match-confirmation-timeout)
12. [Flow 6 — Passenger Cancels Before Match](#12-flow-6--passenger-cancels-before-match)
13. [Flow 7 — Ride Starts (Pickup Phase)](#13-flow-7--ride-starts-pickup-phase)
14. [Flow 8 — Chat Room Lifecycle](#14-flow-8--chat-room-lifecycle)
15. [Flow 9 — Ride Completion](#15-flow-9--ride-completion)
16. [Flow 10 — Mid-Ride Cancellation](#16-flow-10--mid-ride-cancellation)
17. [Payments Integration (Conceptual)](#17-payments-integration-conceptual)
18. [API Reference](#18-api-reference)
19. [Background Workers](#19-background-workers)
20. [Error Handling Conventions](#20-error-handling-conventions)
21. [Open Decisions](#21-open-decisions)

---

## 1. Service Responsibilities

Trip Planner is a **pure orchestrator**. It owns ride state and coordinates other services, but executes no domain logic that belongs elsewhere.

### What Trip Planner owns
- The canonical ride/route state machine (source of truth)
- The PostGIS Trip Store (routes, rides, driver states)
- All ride-scoped Redis keys (see §4)
- Emission of Kafka domain events
- Publication to and consumption from RabbitMQ queues
- Opening/closing of Chat rooms (lifecycle only — no message routing)
- Triggering of price quotes and charge events to Payments

### What Trip Planner does NOT own
- User identity or authentication — delegated to Accounts via gRPC
- Payment execution — delegated to Payments via Kafka events
- Chat message routing — Chat Service owns that entirely
- GPS position tracking — DriverTracker owns that
- Push/SMS/email delivery — Notifications Service owns that
- Rating storage — Accounts Service owns that

### Guiding principle
If Trip Planner goes down, no new rides can be created or matched. Existing in-flight rides should be recoverable from PostGIS on restart. All transient state in Redis must be treated as a cache — PostGIS is the authoritative record.

---

## 2. Ride State Machine

Two parallel state machines exist: one for the **driver's route** and one for the **ride/request** lifecycle. They are linked but independent.

### 2.1 Driver Route States

```
[idle]
   │  POST /api/driver/route
   ▼
[route_pending]          ← Route-Calc job in flight
   │  ResultsQueue delivers
   ▼
[route_active]           ← Driver is visible for matching
   │
   ├─ POST /api/driver/route (modify, no active ride) → [route_pending]
   ├─ DELETE /api/driver/route → [idle]
   ├─ DriverTracker disconnect event → [route_active_offline] (grace period)
   │     └─ reconnect within TTL → [route_active]
   │     └─ TTL expires → [idle]
   └─ Match confirmed → [route_active_in_ride]
         └─ Ride completed/cancelled → [route_active]
```

**Stored in:** `routes` table in PostGIS + `driver:{driver_id}:state` in Redis (cache only).

### 2.2 Ride / Request States

```
[passenger:idle]
   │  POST /api/passenger/route-request
   ▼
[searching]              ← Compute job in flight, SSE channel open
   │  ResultsQueue delivers matches
   ▼
[routes_presented]       ← Ranked routes pushed to passenger via SSE
   │  POST /api/passenger/route-request/{id}  (passenger selects)
   ▼
[pending_driver]         ← Awaiting driver accept/decline
   │  TTL: 30 seconds
   │
   ├─ Driver accepts → [match_confirmed]
   │     │
   │     ▼
   │  [pickup]           ← Price frozen, Chat room open, DriverTracker linked
   │     │  Driver signals arrival
   │     ▼
   │  [awaiting_passenger]
   │     │  Passenger/driver confirms boarding
   │     ▼
   │  [in_ride]          ← Ride is live
   │     │  Driver signals drop-off
   │     ▼
   │  [completed]        ← Charge triggered, Chat closed, ratings prompted
   │
   ├─ Driver declines → [routes_presented] (re-select) or [searching] (re-search)
   ├─ Timeout (30s) → [routes_presented] or [searching]
   ├─ No matches → [no_match] (terminal, passenger must retry)
   └─ Passenger cancels (any pre-confirmed state) → [cancelled]
```

**Terminal states:** `completed`, `no_match`, `cancelled`

**Stored in:** `ride_requests` and `rides` tables in PostGIS + `ride:{ride_id}:state` in Redis (cache).

---

## 3. Data Model

### 3.1 `route_jobs` table

Tracks Route-Calc jobs for both driver route registration and passenger matching.

```sql
CREATE TABLE route_jobs (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    correlation_id  UUID NOT NULL UNIQUE,
    job_type        TEXT NOT NULL CHECK (job_type IN ('driver_route', 'passenger_match')),
    requester_id    UUID NOT NULL,          -- driver_id or passenger_id
    status          TEXT NOT NULL DEFAULT 'pending'
                        CHECK (status IN ('pending', 'completed', 'failed')),
    payload         JSONB NOT NULL,         -- {start, end, constraints}
    result          JSONB,                  -- populated on completion
    error_reason    TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    completed_at    TIMESTAMPTZ,
    expires_at      TIMESTAMPTZ             -- for passenger match results TTL
);

CREATE INDEX ON route_jobs (correlation_id);
CREATE INDEX ON route_jobs (requester_id, status);
```

### 3.2 `driver_routes` table

```sql
CREATE TABLE driver_routes (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    driver_id       UUID NOT NULL UNIQUE,   -- one active route per driver
    status          TEXT NOT NULL DEFAULT 'pending'
                        CHECK (status IN ('pending', 'active', 'cancelled', 'completed')),
    route_geom      GEOMETRY(LineString, 4326),
    start_point     GEOMETRY(Point, 4326) NOT NULL,
    end_point       GEOMETRY(Point, 4326) NOT NULL,
    eta_seconds     INTEGER,
    distance_meters INTEGER,
    job_id          UUID REFERENCES route_jobs(id),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    activated_at    TIMESTAMPTZ,
    cancelled_at    TIMESTAMPTZ
);

CREATE INDEX ON driver_routes (driver_id, status);
CREATE INDEX ON driver_routes USING GIST (route_geom);
CREATE INDEX ON driver_routes USING GIST (start_point);
```

### 3.3 `ride_requests` table

```sql
CREATE TABLE ride_requests (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    passenger_id    UUID NOT NULL,
    status          TEXT NOT NULL DEFAULT 'searching'
                        CHECK (status IN (
                            'searching', 'routes_presented', 'pending_driver',
                            'match_confirmed', 'cancelled', 'no_match'
                        )),
    start_point     GEOMETRY(Point, 4326) NOT NULL,
    end_point       GEOMETRY(Point, 4326) NOT NULL,
    constraints     JSONB NOT NULL DEFAULT '{}',  -- {max_detour_km, preferences}
    match_results   JSONB,                        -- ranked routes from Route-Calc
    selected_route_id UUID REFERENCES driver_routes(id),
    job_id          UUID REFERENCES route_jobs(id),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX ON ride_requests (passenger_id, status);
CREATE INDEX ON ride_requests (selected_route_id);
```

### 3.4 `rides` table

Created when a match is confirmed. This is the authoritative record of an active or completed ride.

```sql
CREATE TABLE rides (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    request_id          UUID NOT NULL REFERENCES ride_requests(id),
    driver_id           UUID NOT NULL,
    passenger_id        UUID NOT NULL,
    driver_route_id     UUID NOT NULL REFERENCES driver_routes(id),
    status              TEXT NOT NULL DEFAULT 'pickup'
                            CHECK (status IN (
                                'pickup', 'awaiting_passenger', 'in_ride',
                                'completed', 'cancelled'
                            )),
    frozen_price_id     UUID,               -- from Payments service
    frozen_price_amount NUMERIC(10,2),
    chat_room_id        UUID,               -- from Chat service
    pickup_point        GEOMETRY(Point, 4326),
    dropoff_point       GEOMETRY(Point, 4326),
    cancelled_by        TEXT CHECK (cancelled_by IN ('driver', 'passenger', 'system')),
    cancellation_phase  TEXT CHECK (cancellation_phase IN (
                            'pre_pickup', 'post_pickup', 'in_ride'
                        )),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    started_at          TIMESTAMPTZ,        -- passenger boards
    completed_at        TIMESTAMPTZ,
    cancelled_at        TIMESTAMPTZ
);

CREATE INDEX ON rides (driver_id, status);
CREATE INDEX ON rides (passenger_id, status);
CREATE INDEX ON rides (request_id);
```

---

## 4. Redis Key Schema

All keys use the prefix `tp:` (trip-planner) to namespace away from other services.

| Key | Type | TTL | Value | Purpose |
|-----|------|-----|-------|---------|
| `tp:driver:{id}:state` | String | none | `idle\|route_pending\|route_active\|route_active_in_ride` | Fast driver state lookup for matching |
| `tp:driver:{id}:route` | String | none | `{route_id, start, end, eta_seconds}` JSON | Active route summary for Route-Calc |
| `tp:active_drivers` | ZSet | none | `driver_id` → timestamp of last GPS update | Set of all matchable drivers |
| `tp:request:{id}:state` | String | 1h | `searching\|routes_presented\|pending_driver\|...` | Fast request state lookup |
| `tp:request:{id}:results` | String | 10m | JSON array of ranked route offers | Match results; expire to prevent stale offers |
| `tp:confirmation:{request_id}` | String | 30s | `driver_id` | Sentinel key — expiry triggers auto-decline |
| `tp:ride:{id}:state` | String | 24h | `pickup\|awaiting_passenger\|in_ride\|completed\|cancelled` | Fast ride state for Chat service reads |
| `tp:driver:{id}:offline_grace` | String | 30s | `route_id` | Set on disconnect; expiry triggers route deactivation |

**Important:** Redis keys are a cache. On Trip Planner restart, rebuild from PostGIS. The background worker `RedisSyncWorker` is responsible for this on startup.

---

## 5. Kafka Events

Trip Planner **publishes** all of these. Other services consume them.

All events share a common envelope:

```json
{
  "event_id": "<uuid>",
  "event_type": "<string>",
  "occurred_at": "<ISO 8601>",
  "payload": { }
}
```

### Published events

| Topic | `event_type` | Publisher | Primary Consumers |
|-------|-------------|-----------|-------------------|
| `ride-completions` | `ride_completed` | TP | Payments, Accounts (ratings prompt) |
| `ride-completions` | `ride_cancelled` | TP | Payments, Notifications |
| `ride-completions` | `ride_interrupted` | TP | Payments, Notifications |
| `user-actions` | `route_requested` | TP | Notifications (→ driver push) |
| `user-actions` | `match_confirmed` | TP | Notifications (→ passenger push) |
| `user-actions` | `match_declined` | TP | Notifications (→ passenger push) |
| `user-actions` | `driver_arrived` | TP | Notifications (→ passenger push) |
| `user-actions` | `ride_started` | TP | Notifications, DriverTracker |
| `notification-triggers` | `rating_prompt` | TP | Notifications |

### Payload schemas

**`ride_completed`**
```json
{
  "ride_id": "<uuid>",
  "driver_id": "<uuid>",
  "passenger_id": "<uuid>",
  "frozen_price_id": "<uuid>",
  "frozen_price_amount": 24.50,
  "distance_meters": 12400,
  "duration_seconds": 1820,
  "completed_at": "2024-01-15T14:32:00Z"
}
```

**`ride_cancelled`**
```json
{
  "ride_id": "<uuid>",
  "driver_id": "<uuid>",
  "passenger_id": "<uuid>",
  "frozen_price_id": "<uuid>",
  "cancelled_by": "passenger",
  "cancellation_phase": "pre_pickup",
  "cancelled_at": "2024-01-15T14:05:00Z"
}
```

**`route_requested`**
```json
{
  "request_id": "<uuid>",
  "driver_id": "<uuid>",
  "passenger_id": "<uuid>",
  "passenger_display_name": "Anna K.",
  "pickup_point": { "lat": 52.2297, "lng": 21.0122 },
  "dropoff_point": { "lat": 52.2489, "lng": 20.9752 },
  "eta_to_passenger_seconds": 420,
  "expires_at": "2024-01-15T14:03:00Z"
}
```

**`ride_started`**
```json
{
  "ride_id": "<uuid>",
  "driver_id": "<uuid>",
  "passenger_id": "<uuid>",
  "started_at": "2024-01-15T14:10:00Z"
}
```

---

## 6. RabbitMQ Contracts

### 6.1 ComputeQueue (publish)

Trip Planner publishes jobs here. Route-Calc consumes them.

**Exchange:** `route-calc-jobs`
**Routing key:** `compute`
**Message properties:** `correlation_id`, `reply_to: "trip-planner-replies"`

**Driver route job payload:**
```json
{
  "job_id": "<uuid>",
  "job_type": "driver_route",
  "driver_id": "<uuid>",
  "start": { "lat": 52.2297, "lng": 21.0122 },
  "end":   { "lat": 52.2489, "lng": 20.9752 }
}
```

**Passenger match job payload:**
```json
{
  "job_id": "<uuid>",
  "job_type": "passenger_match",
  "passenger_id": "<uuid>",
  "start": { "lat": 52.2297, "lng": 21.0122 },
  "end":   { "lat": 52.2489, "lng": 20.9752 },
  "constraints": {
    "max_detour_km": 5,
    "max_results": 5
  }
}
```

### 6.2 ResultsQueue (consume)

Trip Planner's background consumer reads from here.

**Queue:** `trip-planner-replies`
**Correlation:** match on `correlation_id` → look up `route_jobs` table

**Driver route result:**
```json
{
  "job_id": "<uuid>",
  "job_type": "driver_route",
  "status": "completed",
  "route_geom": "<GeoJSON LineString>",
  "eta_seconds": 1820,
  "distance_meters": 12400
}
```

**Passenger match result:**
```json
{
  "job_id": "<uuid>",
  "job_type": "passenger_match",
  "status": "completed",
  "matches": [
    {
      "driver_route_id": "<uuid>",
      "driver_id": "<uuid>",
      "eta_to_passenger_seconds": 420,
      "detour_meters": 800,
      "score": 0.87
    }
  ]
}
```

**Failure (either type):**
```json
{
  "job_id": "<uuid>",
  "status": "failed",
  "error": "no_candidates" | "routing_error" | "timeout"
}
```

**Ack/Nack policy:** ACK after successful DB write. NACK with requeue=false on unrecoverable errors (unknown job_id, malformed payload). Send to dead-letter queue.

---

## 7. Flow 1 — Driver Registers a Route

**Actor:** Driver  
**Precondition:** Driver has a verified account and is logged in. Driver has no currently active route (status = active).

### Sequence

```
Driver App → API Gateway → Trip Planner
                               │
                    1. Validate driver via Accounts gRPC
                    2. Validate service area via PostGIS
                    3. INSERT route_jobs (pending)
                    4. INSERT driver_routes (pending, linked to job)
                    5. Publish to ComputeQueue
                    6. Return 202 {job_id}
                               │
                    [background: ResultsQueue consumer]
                    7. Receive result from ResultsQueue
                    8. UPDATE route_jobs (completed)
                    9. UPDATE driver_routes (active, route_geom, eta_seconds)
                   10. SET tp:driver:{id}:state = route_active
                   11. ZADD tp:active_drivers {driver_id}
                   12. SET tp:driver:{id}:route = {summary JSON}
                               │
Driver App polls: GET /api/driver/route/{job_id}
                   → returns {status, route_geom, eta_seconds} once completed
```

### Validation rules

- **Accounts gRPC `ValidateDriver`:** driver must be `status = active`, `verified = true`, `suspended = false`. If Accounts is unreachable, fail with 503 — do not proceed.
- **Service area:** `ST_Within(ST_SetSRID(ST_MakeLine(start, end), 4326), (SELECT geom FROM service_area WHERE id = 'poland'))`. If either point is outside the service area polygon, return 422 with `error: "outside_service_area"`.
- **Existing active route:** if `driver_routes` has a row for this driver with `status = 'active'`, return 409 with `error: "route_already_active"`. Driver must cancel first.

### API contract

**Request:**
```
POST /api/driver/route
Authorization: Bearer <jwt>

{
  "start": { "lat": 52.2297, "lng": 21.0122 },
  "end":   { "lat": 52.2489, "lng": 20.9752 }
}
```

**Responses:**
```
202 Accepted
{ "job_id": "<uuid>" }

409 Conflict
{ "error": "route_already_active" }

422 Unprocessable Entity
{ "error": "outside_service_area" }

503 Service Unavailable
{ "error": "accounts_unavailable" }
```

**Poll endpoint:**
```
GET /api/driver/route/{job_id}
Authorization: Bearer <jwt>

200 OK  (pending)
{ "status": "pending" }

200 OK  (completed)
{
  "status": "completed",
  "route_id": "<uuid>",
  "route_geom": { <GeoJSON LineString> },
  "eta_seconds": 1820,
  "distance_meters": 12400
}

200 OK  (failed)
{
  "status": "failed",
  "error": "routing_error" | "timeout"
}

404 Not Found
{ "error": "job_not_found" }
```

**Polling guidance for client:** poll every 2s, give up after 60s. If failed, surface error and allow retry.

### Failure paths

| Scenario | Handling |
|----------|----------|
| Route-Calc does not reply within 45s | Background worker marks job `failed`, error = `timeout` |
| Route-Calc replies with `failed` | Mark job `failed`, propagate error reason |
| Driver goes offline during pending | Job continues; result stored when it arrives; driver can poll on reconnect |
| PostGIS write fails | Return 500; do not publish to ComputeQueue |

---

## 8. Flow 2 — Driver Modifies or Cancels a Route

**Actor:** Driver  
**Precondition:** Driver has an active route (`route_active` state). Driver must NOT be in `route_active_in_ride` state.

### 8.1 Route Cancellation

```
DELETE /api/driver/route
Authorization: Bearer <jwt>

Trip Planner:
  1. Verify driver has active route
  2. Verify driver is NOT in_ride (reject if so)
  3. UPDATE driver_routes SET status = 'cancelled'
  4. DEL tp:driver:{id}:state
  5. DEL tp:driver:{id}:route
  6. ZREM tp:active_drivers {driver_id}
  7. Invalidate any pending match requests that listed this driver
     → For each ride_request with status = 'routes_presented' and selected_route_id = this route:
         UPDATE ride_requests SET match_results = (remove this driver from JSON array)
         If match_results becomes empty: transition to 'searching' and re-queue match job
         Notify passenger via Kafka → Notifications: "a driver became unavailable"

204 No Content
```

### 8.2 Route Modification

Route modification is a **cancel + re-register** flow. The same validation and queue flow applies.

```
PUT /api/driver/route
Authorization: Bearer <jwt>

{
  "start": { "lat": ... },
  "end":   { "lat": ... }
}

Trip Planner:
  1. Verify driver is NOT in_ride
  2. Cancel existing route (steps 3–6 from cancellation above)
  3. INSERT new route_jobs + driver_routes
  4. Publish to ComputeQueue
  5. Return 202 {job_id}
```

Do not reuse the old job. Always create a fresh job row.

### Failure paths

| Scenario | Handling |
|----------|----------|
| Driver is in_ride | Return 409 `error: "cannot_modify_during_ride"` |
| Cancellation while pending match — driver has been notified | Cancel the notification. Set ride_request to pending_driver = null, return to routes_presented with that driver removed from results |

---

## 9. Flow 3 — Driver Disconnects

This flow is triggered by DriverTracker, not the driver app directly.

**Trigger:** DriverTracker publishes `driver_disconnected` to Kafka EventQueue.

**Event payload:**
```json
{
  "event_type": "driver_disconnected",
  "driver_id": "<uuid>",
  "last_seen_at": "<ISO 8601>"
}
```

### Trip Planner handling

```
1. Consume driver_disconnected event
2. Read tp:driver:{id}:state from Redis

IF state = 'route_active' (no passenger):
   3a. SET tp:driver:{id}:offline_grace = route_id  (TTL: 30s)
   3b. ZREM tp:active_drivers {driver_id}
       (Driver is removed from matching pool but route not yet cancelled)
   3c. Background: when offline_grace key expires:
       → UPDATE driver_routes SET status = 'cancelled'
       → DEL tp:driver:{id}:state, tp:driver:{id}:route
       → Invalidate pending match requests (same as §8.1 step 7)

IF driver reconnects within grace period:
   → DriverTracker publishes driver_reconnected event
   → Trip Planner: ZADD tp:active_drivers {driver_id}, restore state

IF state = 'route_active_in_ride':
   3a. SET tp:driver:{id}:offline_grace = ride_id  (TTL: 30s)
   3b. Push notification to passenger: "Driver temporarily disconnected"
   3c. Background: when offline_grace key expires:
       → Transition ride to 'cancelled', cancelled_by = 'system'
       → Emit ride_interrupted to Kafka (triggers full refund in Payments)
       → Close Chat room
       → Notify passenger: "Ride was cancelled, you will not be charged"
       → Notify driver: "Ride was cancelled due to disconnection"
```

### Implementing grace period expiry

Redis keyspace notifications require server-side config (`notify-keyspace-events Ex`). For simplicity in Sprint 4, implement a **polling background worker** instead:

- `DisconnectMonitorWorker` runs every 5 seconds
- Queries Redis for all `tp:driver:*:offline_grace` keys
- For each key that has expired (or checks TTL ≤ 0): triggers the expiry logic above
- This avoids Redis config dependency

---

## 10. Flow 4 — Passenger Requests a Match

**Actor:** Passenger  
**Precondition:** Passenger has verified account, logged in. Passenger has no active ride or pending request.

**Matching strategy: A1+B1** — Passenger chooses from ranked routes. Driver's route is NOT modified. See §21 for notes on other variants.

### Sequence

```
Passenger App → API Gateway → Trip Planner
                                  │
                  1. Validate passenger (Accounts gRPC)
                  2. Validate start/end in service area (PostGIS)
                  3. Verify no active request for this passenger
                  4. INSERT ride_requests (status='searching')
                  5. INSERT route_jobs (job_type='passenger_match')
                  6. Publish to ComputeQueue
                  7. Return 202 {request_id}
                  8. Hold SSE connection open for this request_id

[Passenger app opens SSE stream]
GET /api/passenger/route-request/{request_id}/stream

[background: ResultsQueue consumer]
                  9. Receive match result from ResultsQueue
                 10. UPDATE route_jobs (completed), store matches
                 11. UPDATE ride_requests (status='routes_presented'), store match_results JSON
                 12. SET tp:request:{id}:results = match JSON  (TTL: 10min)
                 13. Push SSE event 'routes_ready' to passenger

[Passenger selects a route]
POST /api/passenger/route-request/{request_id}/select
{ "driver_route_id": "<uuid>" }

                 14. Validate driver still active: check tp:driver:{id}:state = 'route_active'
                 15. UPDATE ride_requests (status='pending_driver', selected_route_id)
                 16. SET tp:confirmation:{request_id} (TTL: 30s)
                 17. Emit route_requested to Kafka → Notifications → driver push
                 18. Push SSE event 'awaiting_driver' to passenger

[Driver responds]
POST /api/driver/confirmation/{request_id}
{ "accepted": true }

IF accepted:
                 19. DELETE tp:confirmation:{request_id}  (prevents double-trigger)
                 20. INSERT rides (status='pickup')
                 21. UPDATE ride_requests (status='match_confirmed')
                 22. Call Chat service gRPC: CreateRoom(ride_id, [driver_id, passenger_id])
                 23. Store chat_room_id on rides record
                 24. Request price freeze from Payments gRPC (see §17)
                 25. Store frozen_price_id + amount on rides record
                 26. SET tp:ride:{id}:state = 'pickup'
                 27. SET tp:driver:{id}:state = 'route_active_in_ride'
                 28. Emit match_confirmed to Kafka → Notifications → passenger push
                 29. Push SSE event 'match_confirmed' with {ride_id, chat_room_id, driver_info} to passenger

IF declined:
                 19. DELETE tp:confirmation:{request_id}
                 20. UPDATE ride_requests: remove declined driver from match_results
                 21. Emit match_declined to Kafka → Notifications → passenger push
                 22. Push SSE event 'match_declined' to passenger
                 23. IF remaining results > 0: push updated 'routes_ready' event
                     IF remaining results = 0: transition to 'searching', re-queue match job
```

### SSE event shapes

All SSE events on `/api/passenger/route-request/{id}/stream`:

```
event: routes_ready
data: {
  "request_id": "<uuid>",
  "matches": [
    {
      "driver_route_id": "<uuid>",
      "driver_id": "<uuid>",
      "driver_display_name": "Marek W.",
      "driver_rating": 4.8,
      "eta_to_passenger_seconds": 420,
      "eta_to_destination_seconds": 1820,
      "detour_meters": 800,
      "score": 0.87
    }
  ],
  "expires_at": "<ISO 8601>"
}

event: awaiting_driver
data: { "request_id": "<uuid>", "expires_at": "<ISO 8601 +30s>" }

event: match_confirmed
data: {
  "request_id": "<uuid>",
  "ride_id": "<uuid>",
  "chat_room_id": "<uuid>",
  "driver_display_name": "Marek W.",
  "driver_rating": 4.8,
  "pickup_eta_seconds": 420,
  "frozen_price": 24.50,
  "currency": "PLN"
}

event: match_declined
data: {
  "request_id": "<uuid>",
  "remaining_options": 2
}

event: match_timed_out
data: { "request_id": "<uuid>", "remaining_options": 2 }

event: no_match
data: { "request_id": "<uuid>", "reason": "no_drivers_available" }

event: cancelled
data: { "request_id": "<uuid>" }
```

### Validation on route selection (step 14)

When passenger POSTs a selection, Trip Planner must re-validate before proceeding:
1. Is the `request_id` in `routes_presented` status? (not already pending/confirmed)
2. Does the `driver_route_id` exist in the stored `match_results` for this request?
3. Is `tp:request:{id}:results` key still alive (not expired)?
4. Is `tp:driver:{driver_id}:state = 'route_active'`? (driver still online and available)

If any check fails: return 409 with a reason code, push SSE `routes_expired` event, and re-queue the match job.

### Failure paths

| Scenario | Handling |
|----------|----------|
| No matches found by Route-Calc | UPDATE request status='no_match'. Push SSE `no_match`. Passenger must submit a new request. |
| Route-Calc times out | Same as no_match with reason='routing_timeout' |
| Driver goes offline between result delivery and passenger selection | Step 14 catches this. Push updated routes without that driver, or no_match if none left. |
| SSE connection drops | Client reconnects to the same URL. Trip Planner reads current state from Redis/DB and sends the appropriate last event on reconnect (implement as `Last-Event-ID` support or by re-sending current state on connect). |

---

## 11. Flow 5 — Match Confirmation Timeout

When a passenger selects a driver (step 16 above), Trip Planner sets:

```
SET tp:confirmation:{request_id} = driver_id  EX 30
```

The **ConfirmationMonitorWorker** (background service, runs every 5s) scans for pending confirmations that have passed their deadline.

**Implementation note:** Rather than relying on Redis keyspace expiry events, store the confirmation deadline in the `ride_requests` table:

```sql
ALTER TABLE ride_requests ADD COLUMN confirmation_deadline TIMESTAMPTZ;
```

The worker queries:
```sql
SELECT id, passenger_id, selected_route_id
FROM ride_requests
WHERE status = 'pending_driver'
  AND confirmation_deadline < now()
```

For each expired row:
1. Set status back to `routes_presented` (or `searching` if results also expired)
2. Remove the timed-out driver from `match_results` JSON
3. DELETE `tp:confirmation:{request_id}` from Redis
4. Push SSE event `match_timed_out` to passenger
5. If remaining matches > 0: push `routes_ready` with remaining matches
6. If no remaining matches: set status = `searching`, re-queue match job

The 30-second TTL on the Redis key is a redundant safety net — the DB-driven worker is authoritative.

---

## 12. Flow 6 — Passenger Cancels Before Match

**Actor:** Passenger  
**Precondition:** Request is in any pre-confirmed state: `searching`, `routes_presented`, `pending_driver`

```
DELETE /api/passenger/route-request/{request_id}
Authorization: Bearer <jwt>

Trip Planner:
  1. Verify request belongs to this passenger
  2. Verify status is NOT match_confirmed (too late, use ride cancellation instead)
  3. UPDATE ride_requests SET status = 'cancelled'

  IF status was 'pending_driver':
     4. DELETE tp:confirmation:{request_id}
     5. Emit route_request_withdrawn to Kafka → Notifications
        → Driver push: "Passenger withdrew their request"
        (Driver does NOT need to respond — request is simply withdrawn)

  IF status was 'searching':
     6. The in-flight Route-Calc job will still complete. When it does,
        the ResultsQueue consumer must check the request status before
        pushing to SSE — if cancelled, discard the result silently.

  7. Close SSE stream (push 'cancelled' event then close)
  8. DEL tp:request:{id}:state
  9. DEL tp:request:{id}:results

204 No Content
```

**No payment implications** at this stage. Price is not frozen until `match_confirmed → pickup`.

---

## 13. Flow 7 — Ride Starts (Pickup Phase)

**Precondition:** Ride is in `match_confirmed` state. `frozen_price_id` is set. Chat room is open.

### 13.1 Driver signals arrival at pickup

```
POST /api/driver/ride/{ride_id}/arrived
Authorization: Bearer <jwt>

Trip Planner:
  1. Verify ride belongs to this driver, status = 'pickup'
  2. UPDATE rides SET status = 'awaiting_passenger'
  3. SET tp:ride:{id}:state = 'awaiting_passenger'
  4. Emit driver_arrived to Kafka → Notifications → passenger push
  5. Push SSE event 'driver_arrived' to passenger (if SSE still open)

204 No Content
```

### 13.2 Passenger boards / ride starts

Either party can confirm boarding:

```
POST /api/driver/ride/{ride_id}/start       (driver confirms passenger is in)
POST /api/passenger/ride/{ride_id}/start    (passenger confirms boarding)
Authorization: Bearer <jwt>

Trip Planner:
  1. Verify ride status = 'awaiting_passenger'
  2. UPDATE rides SET status = 'in_ride', started_at = now()
  3. SET tp:ride:{id}:state = 'in_ride'
  4. Emit ride_started to Kafka
     → Notifications: confirmation push to both parties
     → DriverTracker: link ride_id to GPS stream (DriverTracker listens for this event)
  5. Push SSE event 'ride_started' to passenger

204 No Content
```

### 13.3 Price freeze validity

The price freeze (obtained at match_confirmed, see §17) has a TTL. If the TTL expires before the ride starts:

```
Background: PriceFreezMonitorWorker checks rides WHERE status IN ('pickup','awaiting_passenger')
  AND frozen_price_expires_at < now()

IF expired:
  1. Request new price quote from Payments gRPC
  2. UPDATE rides SET frozen_price_id = new_id, frozen_price_amount = new_amount,
                      frozen_price_expires_at = new_expiry
  3. Push SSE event 'price_updated' to passenger:
     { ride_id, new_price, currency, must_reconfirm: true }
  4. Require passenger to POST /api/passenger/ride/{ride_id}/confirm-price
     before the ride can proceed to in_ride
```

This protects against surge changes during long waits. The passenger must explicitly acknowledge the new fare.

---

## 14. Flow 8 — Chat Room Lifecycle

Trip Planner creates and closes Chat rooms. It does not route messages.

### Room creation

Triggered at step 22 of Flow 4 (match confirmed):

```
Trip Planner → Chat Service gRPC: CreateRoom

Request:
{
  "ride_id": "<uuid>",
  "participants": ["<driver_id>", "<passenger_id>"]
}

Response:
{
  "room_id": "<uuid>"
}
```

Store `room_id` on the `rides` record. The `room_id` is included in the `match_confirmed` SSE event so the passenger app can open the Chat WebSocket immediately.

### Message gating

Chat Service must reject messages for closed or non-existent rooms. It does this by reading `tp:ride:{ride_id}:state` from Redis before accepting a message:

- `pickup` → allow (driver and passenger need to coordinate pickup)
- `awaiting_passenger` → allow
- `in_ride` → allow
- `completed`, `cancelled` → reject with error `room_closed`
- key missing → reject with error `room_not_found`

**This means Chat reads from Trip Planner's Redis keyspace.** The key `tp:ride:{id}:state` is the shared contract. Trip Planner must ensure this key is always set for active rides and deleted (or set to terminal state) after completion.

### Room closure

Trip Planner closes the room by updating the ride state:

```
On ride completed or cancelled:
  SET tp:ride:{id}:state = 'completed' | 'cancelled'
  → Chat will reject further messages
  → Room remains readable (messages retained 30 days per policy)

Trip Planner → Chat Service gRPC: CloseRoom (optional, for explicit cleanup)
{
  "room_id": "<uuid>",
  "reason": "ride_completed" | "ride_cancelled"
}
```

Trip Planner must emit the state change **before** emitting the Kafka event, so Chat has the correct state before any client-side UI transition happens.

---

## 15. Flow 9 — Ride Completion

**Actor:** Driver (primary signal)  
**Precondition:** Ride is in `in_ride` state.

```
POST /api/driver/ride/{ride_id}/complete
Authorization: Bearer <jwt>

{
  "dropoff_point": { "lat": 52.2489, "lng": 20.9752 }
}

Trip Planner:
  1. Verify ride belongs to this driver, status = 'in_ride'
  2. Optionally validate dropoff_point plausibility:
     ST_DWithin(dropoff_point, ride.end_point, 500)  -- within 500m of expected destination
     (warn but don't reject if outside — driver may have adjusted)
  3. UPDATE rides SET status='completed', completed_at=now(), dropoff_point=...
  4. UPDATE driver_routes: status remains 'active' (driver continues their route)
     OR set back to 'route_active' if the ride was their final stop — TBD per business logic
  5. SET tp:ride:{id}:state = 'completed'   ← BEFORE emitting events
  6. SET tp:driver:{id}:state = 'route_active'
  7. DEL tp:driver:{id}:offline_grace (if present)

  8. Emit ride_completed to Kafka (payload: see §5)
     → Payments consumes: charges frozen_price_id
     → Accounts consumes: triggers rating prompts via Notifications

  9. Call Chat Service gRPC: CloseRoom (or just let state gate it)
 10. DEL tp:request:{id}:results
 11. DEL tp:confirmation:{request_id} (safety cleanup)

 12. Push SSE event 'ride_completed' to passenger:
     { ride_id, amount_charged, currency, driver_display_name }

204 No Content
```

### Post-completion

Rating prompts are sent by Notifications Service after consuming `ride_completed` from Kafka. Ratings are submitted to and stored in Accounts Service. Trip Planner is not involved in this.

---

## 16. Flow 10 — Mid-Ride Cancellation

Allowed from states: `pickup`, `awaiting_passenger`, `in_ride`.

### 16.1 Passenger cancels

```
DELETE /api/passenger/ride/{ride_id}
Authorization: Bearer <jwt>

{ "reason": "<optional string>" }
```

### 16.2 Driver cancels

```
DELETE /api/driver/ride/{ride_id}
Authorization: Bearer <jwt>

{ "reason": "<optional string>" }
```

### Shared cancellation logic

```
Trip Planner:
  1. Determine cancellation_phase based on current ride status:
     - status = 'pickup' or 'awaiting_passenger' → phase = 'pre_pickup'
     - status = 'in_ride' → phase = 'in_ride'

  2. UPDATE rides SET
       status = 'cancelled',
       cancelled_by = '<driver|passenger>',
       cancellation_phase = '<phase>',
       cancelled_at = now()

  3. SET tp:ride:{id}:state = 'cancelled'    ← BEFORE emitting events
  4. SET tp:driver:{id}:state = 'route_active'   (driver is free again)
  5. UPDATE ride_requests SET status = 'cancelled'

  6. Emit ride_cancelled to Kafka (payload: see §5)
     → Payments: applies cancellation fee logic based on cancelled_by + cancellation_phase
     → Notifications: push to the OTHER party

  7. Push SSE event 'ride_cancelled' to the other party:
     { ride_id, cancelled_by, reason }

204 No Content
```

### Cancellation fee policy (for Payments)

Trip Planner does NOT calculate fees. It sends structured context; Payments decides the amount:

| `cancelled_by` | `cancellation_phase` | Payments action |
|----------------|---------------------|-----------------|
| `passenger` | `pre_pickup` | Release freeze (no charge) |
| `passenger` | `in_ride` | Partial charge (configurable in Payments) |
| `driver` | any | Full release + driver penalty (Payments/Accounts) |
| `system` | any | Full release (ride_interrupted path) |

---

## 17. Payments Integration (Conceptual)

Trip Planner's contract with Payments is intentionally minimal. Trip Planner provides context; Payments executes all financial logic.

### 17.1 Price quote request

Called at `match_confirmed` → immediately before creating the ride record.

**gRPC call: `Payments.QuotePrice`**

```protobuf
message QuotePriceRequest {
  string driver_route_id = 1;
  string passenger_id = 2;
  double pickup_lat = 3;
  double pickup_lng = 4;
  double dropoff_lat = 5;
  double dropoff_lng = 6;
  int32  distance_meters = 7;
  int32  eta_seconds = 8;
  string currency = 9;           // "PLN"
}

message QuotePriceResponse {
  string frozen_price_id = 1;
  double amount = 2;
  string currency = 3;
  int64  expires_at_unix = 4;    // Unix timestamp; TTL for this quote
}
```

**On success:** store `frozen_price_id`, `frozen_price_amount`, `frozen_price_expires_at` on the ride record.

**On failure:** do not create the ride. Push SSE `match_failed` to passenger with `reason: "pricing_unavailable"`. This should be treated like a match decline — passenger can retry.

### 17.2 Charge trigger

Trip Planner emits `ride_completed` to Kafka. Payments Service is a consumer. Payments independently executes the charge using `frozen_price_id`.

**Trip Planner does not call Payments on completion.** It only emits the event. Payments is responsible for consuming it reliably (idempotent consumer pattern).

### 17.3 Freeze release / partial charge trigger

Trip Planner emits `ride_cancelled` or `ride_interrupted` to Kafka with `cancelled_by` and `cancellation_phase`. Payments decides the charge amount based on its own fee schedule.

**Trip Planner never knows the final charged amount.** If the passenger app needs to show a receipt, it should query Payments directly (via API Gateway → Payments), not Trip Planner.

---

## 18. API Reference

Full list of Trip Planner HTTP endpoints. All require `Authorization: Bearer <jwt>` unless marked public.

### Driver endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/driver/route` | Register a new route |
| `GET` | `/api/driver/route/{job_id}` | Poll route registration status |
| `PUT` | `/api/driver/route` | Modify route (cancel + re-register) |
| `DELETE` | `/api/driver/route` | Cancel active route |
| `POST` | `/api/driver/confirmation/{request_id}` | Accept or decline a passenger match |
| `POST` | `/api/driver/ride/{ride_id}/arrived` | Signal arrival at pickup point |
| `POST` | `/api/driver/ride/{ride_id}/start` | Confirm passenger has boarded |
| `POST` | `/api/driver/ride/{ride_id}/complete` | Signal ride completion |
| `DELETE` | `/api/driver/ride/{ride_id}` | Cancel active ride |

### Passenger endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/passenger/route-request` | Submit a match request |
| `GET` | `/api/passenger/route-request/{id}/stream` | SSE stream for request updates |
| `POST` | `/api/passenger/route-request/{id}/select` | Select a route from ranked results |
| `DELETE` | `/api/passenger/route-request/{id}` | Cancel before match confirmed |
| `POST` | `/api/passenger/ride/{ride_id}/start` | Confirm boarding |
| `POST` | `/api/passenger/ride/{ride_id}/confirm-price` | Re-confirm after price freeze expiry |
| `DELETE` | `/api/passenger/ride/{ride_id}` | Cancel active ride |

### Common response envelope

All error responses:
```json
{
  "error": "<error_code>",
  "message": "<human readable>",
  "details": { }
}
```

### Error codes

| Code | HTTP Status | Meaning |
|------|-------------|---------|
| `route_already_active` | 409 | Driver tried to register while one is active |
| `outside_service_area` | 422 | Route points outside Poland polygon |
| `accounts_unavailable` | 503 | Accounts gRPC unreachable |
| `payments_unavailable` | 503 | Payments gRPC unreachable |
| `cannot_modify_during_ride` | 409 | Driver tried to modify route while in_ride |
| `no_active_request` | 404 | No matching request found for this passenger |
| `request_expired` | 409 | Match results TTL expired before passenger selected |
| `driver_unavailable` | 409 | Selected driver went offline before confirmation |
| `invalid_state_transition` | 409 | Action not allowed in current state |
| `not_found` | 404 | Resource does not exist |
| `forbidden` | 403 | Resource belongs to a different user |

---

## 19. Background Workers

Trip Planner runs the following background services. Each should be implemented as a hosted service (`IHostedService` in .NET).

### 19.1 ResultsQueueConsumer

- Listens on `trip-planner-replies` RabbitMQ queue
- For each message: look up `route_jobs` by `correlation_id`
- Dispatch to `DriverRouteResultHandler` or `PassengerMatchResultHandler` based on `job_type`
- ACK after successful DB write; NACK + dead-letter on error

### 19.2 KafkaEventConsumer

- Listens on Kafka topics for events from other services
- Relevant events:
  - `driver_disconnected` (from DriverTracker) → Flow 3
  - `driver_reconnected` (from DriverTracker) → cancel grace period

### 19.3 ConfirmationMonitorWorker

- Runs every 5 seconds
- Queries ride_requests WHERE `status = 'pending_driver' AND confirmation_deadline < now()`
- Triggers timeout logic (see Flow 5)

### 19.4 DisconnectMonitorWorker

- Runs every 5 seconds
- Checks `driver_routes` WHERE status = 'active' and driver has an `offline_grace` entry in Redis whose deadline has passed
- Triggers disconnect cleanup (see Flow 3)

### 19.5 PriceFreezeMonitorWorker

- Runs every 30 seconds
- Queries rides WHERE `status IN ('pickup', 'awaiting_passenger') AND frozen_price_expires_at < now() + interval '2 minutes'`
- Re-quotes price from Payments and notifies passenger (see §13.3)

### 19.6 RedisSyncWorker

- Runs once on startup
- Reads all active driver routes, active rides, and pending requests from PostGIS
- Rebuilds Redis keys to match current DB state
- Prevents stale Redis after pod restart

---

## 20. Error Handling Conventions

### Idempotency

All state-modifying endpoints must be idempotent:
- `POST /api/driver/confirmation/{request_id}` with `accepted: true`, called twice → second call returns 200 (already accepted), no duplicate ride created
- `POST /api/driver/ride/{ride_id}/complete`, called twice → second call returns 200 (already completed), no duplicate Kafka event

Pattern: check current state first, return success if already in target state, error only if in an incompatible state.

### Kafka event deduplication

Emit Kafka events **after** the DB write, not before. If the DB write fails, no event is emitted. If the DB write succeeds but Kafka publish fails: the DB is the source of truth — a retry or reconciliation worker can re-emit based on DB state.

For critical events (`ride_completed`, `ride_cancelled`), use an outbox pattern:
1. Write event to a `kafka_outbox` table in the same DB transaction as the state change
2. A separate `KafkaOutboxPublisher` worker reads and publishes pending events, marks them published

This prevents the lost-event scenario where the process crashes between DB write and Kafka publish.

### gRPC timeouts

| Call | Timeout | On timeout |
|------|---------|------------|
| `Accounts.ValidateDriver` | 3s | Return 503 |
| `Chat.CreateRoom` | 3s | Retry once, then fail match with reason `chat_unavailable` |
| `Payments.QuotePrice` | 5s | Retry once, then fail match with reason `pricing_unavailable` |

### Concurrency

The `rides` table uses optimistic locking for state transitions to prevent race conditions:

```sql
UPDATE rides
SET status = 'in_ride', started_at = now()
WHERE id = $1 AND status = 'awaiting_passenger'
RETURNING id;
```

If `RETURNING` returns no rows, the transition was already made by another request — return 200 (idempotent) if the current state matches the target, or 409 if in an incompatible state.

---

## 21. Open Decisions

The following items require team agreement before implementation. Decisions should be recorded back into this document.

| # | Question | Options | Recommendation |
|---|----------|---------|----------------|
| 1 | **Matching strategy for v1** | A1+B1 (passenger picks, route unchanged) vs others | **Use A1+B1.** Simplest complete flow. Other variants are v2. |
| 2 | **Auto-retry on no match / declined** | Auto-re-queue silently vs prompt passenger to retry | Prompt passenger. Silent retry can loop indefinitely on bad conditions. |
| 3 | **Decline loop limit** | How many driver declines before giving up? | Suggest 3. After 3 declines, transition to `no_match` and require new request. |
| 4 | **Cancellation fee tiers** | What are the thresholds? | TBD with business. Pre-pickup: no fee. Post-pickup, pre-board: no fee. In-ride: configurable flat fee or % of fare. |
| 5 | **Price freeze TTL** | How long should a frozen price be valid? | Suggest 15 minutes. Long enough for realistic pickups, short enough to catch surge changes. |
| 6 | **Route modified post-boarding** | Can driver deviate significantly without renegotiation? | Out of scope for v1. B2 variant handles this. |
| 7 | **Outbox pattern** | Required for v1, or defer? | Implement for `ride_completed` and `ride_cancelled` only (financial events). Defer for others. |
| 8 | **Chat.CreateRoom gRPC vs event-driven** | gRPC sync call from TP, or TP emits event and Chat reacts? | Use gRPC for Sprint 4 — simpler, explicit, easier to debug. Migrate to event-driven later. |
| 9 | **Driver route post-completion** | Does driver's route persist after a ride, or is it consumed? | Persist. Driver registered a route to drive from A to B — completing a passenger segment doesn't change their destination. |