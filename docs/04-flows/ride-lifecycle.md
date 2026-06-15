# Flow — Ride lifecycle

The end-to-end driver↔passenger flow, from a driver publishing a route to the ride
completing or being cancelled. This is the heart of Trip Planner; the canonical
domain spec is in [reference/trip-planner-spec.md](../reference/trip-planner-spec.md).

**Status legend:** ✅ implemented · 🟡 partial / via fake · ⬜ deferred.

## Aggregates & states

| Aggregate | States |
|-----------|--------|
| **Route** | `Calculating → Created → Active → Full` |
| **Ride** | `WaitingForActivation → WaitingForDriver → Started` (→ removed on end) |
| **RideRequest** | `Pending → Accepted / Rejected` |

## The flow

### ✅ flow 0 — driver creates a Route
1. Driver publishes a route → `Route(status = Calculating)` inserted + a compute job
   queued to RabbitMQ.
2. route-calc computes geometry; `RouteComputedHandler` sets it, `status = Created`.
   *(async — the create call returns immediately; geometry arrives via the result queue.)*

### ✅ flow 1 — driver activates the Route
- Validates the driver is at the start, sets `status = Active`, activates any rides
  that were `WaitingForActivation`.

### ✅ flow 1.5 — driver deletes the Route
- `Active` **with** rides → blocked.
- `Created` **with** rides → deleted, `RouteDeleted` emitted, passengers notified.
- no rides → deleted outright.

### ✅ flow 2 — passenger searches routes
1. Passenger submits `start`/`end` → compute job queued (async).
2. route-calc returns top-N non-`Full` routes scored by a route-overlap metric
   (passenger start/end vs driver current location). Result delivered via
   `RouteSearchCompletedEvent`.

### ✅ flow 3 — passenger picks a route (creates a RideRequest)
1. Reject if the route is `Full`.
2. `CreateRideRequestHandler` quotes the price and **freezes** it 🟡 (fake payments),
   inserts `RideRequest(Pending)`.
3. `RideRequested` → driver notified (SSE ✅; push ⬜).

### ✅ flow 4 — driver rejects
- `RejectRideRequestHandler` marks the request `Rejected`, **unfreezes** funds 🟡,
  emits `RideRejected` → passenger notified.

### ✅ flow 5 — driver accepts (atomic)
All inside a transaction with `SELECT … FOR UPDATE` on the route (prevents
double-booking):
1. Create `Ride` — `WaitingForDriver` if the route is already `Active`, else
   `WaitingForActivation`.
2. If seats now exhausted → route `Full`, **reject all other pending requests**.
3. Open a chat room 🟡 (fake chat).
4. On any failure → auto-reject (as flow 4).
5. `RideAccepted` → passenger notified.

### ⬜ flow 6 — rejected RideRequest TTL (24 h)
Deferred. Requests are not auto-purged; the "seat freed" re-notification is not built.

### ⬜ flow 6.5 — ride meeting timeout
Deferred (too complex for the first cut): driver no-show → no charge + ride removed;
passenger no-show → `cancellation_price` + ride removed.

### ✅ flow 7 — ride start (symmetric handshake)
1. Driver declares pickup (`DeclareDriverPickup`).
2. Passenger declares pickup (`DeclarePassengerPickup`). A passenger declaration
   **before** the driver's is ignored.
3. After both → `Ride.status = Started` (timeout no longer applies).

### ✅ flow 8 — end / cancel
Depends on state when cancelled:
- `WaitingForActivation` → no charge, ride removed.
- `WaitingForDriver` → `cancellation_price` charged 🟡, ride removed.
- `Started` → symmetric end handshake (`DeclarePassengerEnd` + `DeclareDriverEnd`),
  then price charged 🟡, ride removed.

Common: `RideEnded` emitted → passengers with **rejected** requests on that route are
notified (a seat may have freed up). *(Notification target wiring is partial.)*

## Payments & funds

Money moves through `IPaymentsService` (🟡 `FakePaymentsService` today — see
[payments.md](../03-components/payments.md)):

| Step | Action |
|------|--------|
| flow 3 (request) | **freeze** `price` |
| flow 4 (reject) | **unfreeze** |
| flow 5 (accept) | funds stay frozen |
| flow 8 — cancel before pickup (`WaitingForActivation`) | unfreeze, no charge |
| flow 8 — cancel after activation (`WaitingForDriver`) | charge `cancellation_price` |
| flow 8 — complete (`Started`) | charge `price` |

## Events → notifications

| Event | Recipient |
|-------|-----------|
| `RideRequested` | driver |
| `RideRejected` | passenger |
| `RideAccepted` | passenger |
| `RouteDeleted` | passengers with rides on the route |
| `RideEnded` | passengers with rejected requests on the route |

Events are published to Kafka and delivered to open browser sessions over SSE (push
to offline devices is planned). See [notifications-sse](notifications-sse.md).

## Known gaps (flagged for backend work)
- `reject` → decline metric and `cancel` → cancelled metric are not yet wired to the
  ride-transition counters.
- Route geometry currently returns only endpoints (2 points), not the full OSRM
  linestring.
- Chat + Payments are fakes; the timeout (6.5) and request-TTL (6) flows are deferred.
