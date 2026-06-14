# PullApp — Containers (C4 Level 2)

The deployable units, their technology, how they communicate, and what data they
own. See the picture in [diagram.md](diagram.md) and the runtime mapping in
[deployment.md](deployment.md).

**Legend:** ✅ implemented & running · 🟡 partial · ⬜ planned.

> **Reality check vs. the original design:** client→service and gateway→service
> traffic is **HTTP/REST over YARP**, not gRPC. The gRPC topology was the original
> intent but the implemented services expose minimal-API REST endpoints, and the
> gateway is a YARP reverse proxy that forwards HTTP. Async matching still goes
> over RabbitMQ. This doc describes what's actually deployed.

---

## ✅ Frontend (web client)

| Attribute | Description |
|-----------|-------------|
| **Purpose** | Passenger + driver web UI: auth, route publish/search, ride requests, my-trips, live notifications |
| **Technology** | Next.js (App Router), turborepo + pnpm; packages `@pullapp/{domain,api-client,features,ui}`; zustand (+persist) for client state |
| **Communication** | HTTPS to gateway (REST), SSE for notifications (`/sse/notifications`) |
| **Scaling** | Stateless (static + server components) |
| **Data** | None server-side; JWT + UI state in browser (localStorage via zustand persist) |
| **Component doc** | [frontend.md](../03-components/frontend.md) |

---

## ✅ API Gateway

| Attribute | Description |
|-----------|-------------|
| **Purpose** | Single entry point for all client traffic |
| **Technology** | .NET 10 + **YARP** reverse proxy |
| **Communication** | HTTPS (clients) → HTTP (internal). Validates JWT, injects `X-User-Id` / `X-User-Role` downstream |
| **Scaling** | Horizontal, stateless |
| **Data** | None |
| **Component doc** | [gateway.md](../03-components/gateway.md) |

Routes (YARP): `/api/auth/**` and `/api/users/**` → accounts; `/api/route/**` →
trip-planner (prefix stripped); `/sse/notifications` → notifications; `/api/tracker/**`
+ `/ws/driver-tracker/**` → driver-tracker.

---

## ✅ Accounts

| Attribute | Description |
|-----------|-------------|
| **Purpose** | User identity, registration, authentication, profile (`/me`) |
| **Technology** | .NET 10, Clean Architecture |
| **Communication** | HTTP (REST) via gateway. **Issues JWTs** (HS256, `kid=pullapp-key`) |
| **Scaling** | Horizontal, stateless |
| **Data** | Owns PostgreSQL (users, credentials) |
| **Component doc** | [accounts.md](../03-components/accounts.md) |

---

## ✅ Trip Planner

| Attribute | Description |
|-----------|-------------|
| **Purpose** | Ride orchestration & canonical state: routes, rides, ride requests |
| **Technology** | .NET 10, Clean Architecture, raw Dapper over PostGIS |
| **Communication** | HTTP (REST) via gateway; **RabbitMQ** (publish compute jobs / consume results); **Kafka** (publish domain events) |
| **Scaling** | Horizontal, stateless |
| **Data** | Owns PostgreSQL + PostGIS; pessimistic `SELECT … FOR UPDATE` on accept |
| **Component doc** | [trip-planner.md](../03-components/trip-planner.md) |

---

## ✅ Route-Calc

| Attribute | Description |
|-----------|-------------|
| **Purpose** | Distance matrices, driver scoring, optimal matching; route geometry |
| **Technology** | Python 3.13 (FastAPI + aio-pika) + C++20 (OSRM via pybind11) |
| **Communication** | **RabbitMQ** consumer (compute jobs) → results published back to RabbitMQ |
| **Scaling** | **KEDA** auto-scaling on RabbitMQ queue depth (scale-to-zero when idle) |
| **Data** | Reads OSM extract (in-memory routing graph); no DB ownership |
| **Component doc** | [route-calc.md](../03-components/route-calc.md) |

**Resource note (region = Poland):** ~8 GB RAM + ~4 CPU per instance for the OSRM graph.

---

## 🟡 Notifications

| Attribute | Description |
|-----------|-------------|
| **Purpose** | Deliver domain events to clients: **SSE** to open browser sessions (live), push to offline devices (planned) |
| **Technology** | Go (Kafka consumer) — SSE hub implemented; FCM push planned |
| **Communication** | SSE (`/stream`, exposed as `/sse/notifications` via gateway), Kafka (consumer), FCM (planned) |
| **Scaling** | SSE hub holds connections; horizontal scaling needs sticky routing (planned) |
| **Data** | device_tokens + idempotency log (planned) |
| **Component doc** | [notifications.md](../03-components/notifications.md) |

---

## 🟡 Driver Tracker

| Attribute | Description |
|-----------|-------------|
| **Purpose** | GPS ingest (`POST /position`) + live position streaming (`WS /track/{routeId}`) |
| **Technology** | Go |
| **Communication** | HTTP (driver POST), WebSocket (passenger), Redis R/W |
| **Scaling** | Stateless POST; WS needs sticky sessions |
| **Data** | Redis only (`position:{routeId}`, 30 s TTL); trip-planner deletes the key on ride end |
| **Component doc** | [driver-tracker.md](../03-components/driver-tracker.md) |

---

## ⬜ Chat

| Attribute | Description |
|-----------|-------------|
| **Purpose** | Ride-scoped real-time messaging between driver and passenger |
| **Technology** | Go (planned) |
| **Communication** | gRPC room lifecycle (trip-planner creates/closes rooms), WebSocket (clients) |
| **Data** | MongoDB (messages, 30 d TTL), Redis (room metadata) |
| **Status** | Not implemented — trip-planner calls `FakeChatService` |
| **Component doc** | [chat.md](../03-components/chat.md) |

---

## ⬜ Payments

| Attribute | Description |
|-----------|-------------|
| **Purpose** | Freeze/charge/refund ride price + cancellation fee, wallets |
| **Technology** | .NET (planned) |
| **Communication** | HTTP/gRPC internal, HTTPS to payment gateway |
| **Data** | Owns PostgreSQL ledger |
| **Status** | Not implemented — trip-planner calls `FakePaymentsService` |

---

## ⬜ Tile Server

| Attribute | Description |
|-----------|-------------|
| **Purpose** | Serve vector map tiles (Poland) + MapLibre style |
| **Technology** | TileServer GL (Node.js) + Nginx cache (planned) |
| **Data** | MBTiles file + CDN |
| **Status** | Not implemented |

---

## Data stores

| Store | Technology | Purpose | Owned by |
|-------|------------|---------|----------|
| Trip store | PostgreSQL + PostGIS | routes, rides, ride_requests, route_jobs, service_area | Trip Planner |
| User store | PostgreSQL | users, credentials | Accounts |
| Cache | Redis | ride sessions, route cache, rate limits | shared |
| Position cache | Redis (`position:{routeId}`, 30 s TTL) | live driver GPS | Driver Tracker (write), Trip Planner (delete) |
| Ledger | PostgreSQL | transactions, wallets | Payments ⬜ |
| Message store | MongoDB | chat messages, 30 d TTL | Chat ⬜ |
| Device tokens / notif log | PostgreSQL | FCM tokens + idempotency | Notifications 🟡 |
| Tile storage | MBTiles + CDN | map tiles | Tile Server ⬜ |

In the local cluster, databases/cache/queues are **not** in-cluster — they run in
docker-compose and are reached via Kubernetes `ExternalName` services pointing at
`host.minikube.internal`. See [deployment.md](deployment.md).

## Synchronous vs asynchronous boundaries

| Operation | Pattern | Rationale |
|-----------|---------|-----------|
| Route search / matching | **Async** (RabbitMQ) | compute-heavy, KEDA-scaled, deferrable |
| Ride request / accept / reject / cancel | **Sync** (REST) | user expects immediate confirmation; accept uses row lock |
| Domain events → notifications | **Async** (Kafka → SSE/push) | fire-and-forget |
| Live driver position | **Sync** (Redis + WS) | real-time tracking |
