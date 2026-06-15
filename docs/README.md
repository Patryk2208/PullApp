# PullApp — Documentation

Architecture and design docs for **PullApp**, a ride-sharing platform built as a
monorepo of microservices. The docs are organised around the
[C4 model](https://c4model.com/): each level zooms in one step further.

| Level | Question it answers | Folder |
|-------|--------------------|--------|
| **L1 — Context** | Who uses the system and what does it talk to? | [`01-context/`](01-context/) |
| **L2 — Containers** | What are the deployable units and how do they communicate? | [`02-containers/`](02-containers/) |
| **L3 — Components** | What's inside each service? | [`03-components/`](03-components/) |
| **Flows** | How do the containers collaborate to deliver a use case? | [`04-flows/`](04-flows/) |
| **Observability** | How do we see what the running system is doing? | [`05-observability/`](05-observability/) |
| **Reference** | Long-form specs and process notes | [`reference/`](reference/) |

> C4 has a fourth level (Code). We deliberately stop at Components — class-level
> structure lives in the source and would go stale instantly. Component docs link
> to the relevant source directories instead.

## Map

### L1 — Context
- [System context](01-context/system-context.md) — actors (Passenger, Driver), external systems (FCM, OSM/OSRM, payment gateway).

### L2 — Containers
- [Containers](02-containers/containers.md) — every deployable service, its tech, comms, and data ownership.
- [Container diagram](02-containers/diagram.md) — the picture (Mermaid).
- [Deployment](02-containers/deployment.md) — how containers map to Kubernetes (minikube) + docker-compose infra.

### L3 — Components
Per service. **Bold = implemented and running**; the rest are designed/stubbed.

- **[Frontend](03-components/frontend.md)** — Next.js web client (turborepo + pnpm).
- **[Gateway](03-components/gateway.md)** — YARP reverse proxy, JWT validation, header injection.
- **[Accounts](03-components/accounts.md)** — identity, auth (JWT issuer), `/me` profile.
- **[Trip Planner](03-components/trip-planner.md)** — ride orchestration, the three aggregates, read-model endpoints.
- **[Route-Calc](03-components/route-calc.md)** — Python + C++/OSRM matching engine, KEDA-autoscaled.
- [Notifications](03-components/notifications.md) — SSE hub (implemented) + Kafka→push (planned).
- [Driver Tracker](03-components/driver-tracker.md) — GPS ingest + live position streaming.
- [Chat](03-components/chat.md) — ride-scoped messaging (planned, Go).
- [Payments](03-components/payments.md) — wallet / freeze / charge (planned; trip-planner uses a fake).

### Flows
- [Ride lifecycle](04-flows/ride-lifecycle.md) — the end-to-end driver↔passenger flow (flows 0–8), with implementation status per step.
- [Auth & profile](04-flows/auth-and-profile.md) — register → login → JWT → gateway → `/me`.
- [Notifications (SSE)](04-flows/notifications-sse.md) — how domain events reach the browser.

### Observability
- [Overview](05-observability/observability.md) — OTLP → collector → Prometheus / Loki / Tempo → Grafana.
- [Metrics](05-observability/metrics.md) — the metrics actually emitted by each service (real names).
- [Dashboards](05-observability/dashboards.md) — the three dashboards provisioned into Grafana.

### Reference
- [Trip Planner spec](reference/trip-planner-spec.md) — the detailed domain spec.
- [GitFlow](reference/gitflow.md) — branching model.
- [Sprint 4](reference/sprint-4.md) — sprint notes.

## Status at a glance

| Service | State | Notes |
|---------|-------|-------|
| accounts | ✅ running | JWT issuer, register/login/`me` |
| gateway | ✅ running | YARP, JWT validation, `X-User-*` injection |
| trip-planner | ✅ running | all ride flows + read-model GETs |
| route-calc | ✅ running | KEDA-autoscaled (scale-to-zero) |
| frontend | ✅ running | passenger + driver flows, SSE |
| notifications | 🟡 partial | SSE hub live; Kafka→FCM push planned |
| driver-tracker | 🟡 designed | container + routes wired, Go service stub |
| chat | ⬜ planned | trip-planner calls a fake |
| payments | ⬜ planned | trip-planner calls a fake |
| tile-server | ⬜ planned | — |
