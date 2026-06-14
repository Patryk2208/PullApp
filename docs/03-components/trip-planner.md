# Trip Planner — Components (C4 Level 3)

Source: `src/services/trip-planner/`

## Opis

Trip Planner is the ride-orchestration service. It owns the canonical state of every route, ride, and ride request, and coordinates all external services (Accounts, Payments, Chat, route-calc) without performing their domain logic itself.

## Architektura

Clean Architecture: `Domain` → `Application` → `Infrastructure` → `Api`.

- **Domain** — three DDD aggregates with private setters and no infrastructure dependencies:
  - `Route` — `Calculating → Created → Active → Full`
  - `Ride` — `WaitingForActivation → WaitingForDriver → Started`
  - `RideRequest` — `Pending → Accepted / Rejected`
- **Application** — one handler per use case (`IHandler<TCommand, TResult>`), repository interfaces, service interfaces. No framework references.
- **Infrastructure** — Postgres/PostGIS repositories (raw Dapper via `DbSession`), `RabbitComputePublisher`, `KafkaEventPublisher`, `PostgisGeoService`, `Fake*` stubs for unimplemented services.
- **Api** — minimal API endpoints (`IEndpoint`), `ExceptionMiddleware`, health checks, `BackgroundServices/` for hosted RabbitMQ consumer and Kafka consumer.

## Handlers

### Driver
| Handler | Command | Side effects |
|---------|---------|--------------|
| `CreateRouteHandler` | `CreateRouteCommand` | Inserts Route + RouteJob, publishes compute job to RabbitMQ |
| `ActivateRouteHandler` | `ActivateRouteCommand` | Route → Active, activates waiting rides |
| `DeleteRouteHandler` | `DeleteRouteCommand` | Deletes route + rides + requests, unfreezes payments, emits events |
| `AcceptRideRequestHandler` | `AcceptRideRequestCommand` | `SELECT FOR UPDATE` route, creates Ride, rejects pending if Full, opens chat room |
| `RejectRideRequestHandler` | `RejectRideRequestCommand` | Marks request Rejected, unfreezes payment |
| `DeclareDriverPickupHandler` | `DeclareDriverPickupCommand` | Sets `DriverDeclaredPickup` flag |
| `DeclareDriverEndHandler` | `DeclareDriverEndCommand` | Sets `DriverDeclaredEnd`; if passenger also declared → EndedAt, route RemoveRide, charge triggered |

### Passenger
| Handler | Command | Side effects |
|---------|---------|--------------|
| `SubmitRouteSearchHandler` | `SubmitRouteSearchCommand` | Inserts RouteJob, publishes compute job |
| `CreateRideRequestHandler` | `CreateRideRequestCommand` | Quotes & freezes price, inserts RideRequest |
| `CancelRideHandler` | `CancelRideCommand` | Cancels ride (phase-dependent payment), emits event |
| `DeclarePassengerPickupHandler` | `DeclarePassengerPickupCommand` | Sets `PassengerDeclaredPickup`; if driver also declared → Started |
| `DeclarePassengerEndHandler` | `DeclarePassengerEndCommand` | Sets `PassengerDeclaredEnd` |

### Background
| Handler | Trigger | Side effects |
|---------|---------|--------------|
| `RouteComputedHandler` | RabbitMQ result message | DriverRoute → sets geometry on Route; PassengerMatch → marks job complete, emits `RouteSearchCompletedEvent` |

## HTTP endpoints

Paths are the bare service paths; the gateway exposes them under `/api/route/**`.
`X-User-Id` (injected by the gateway) identifies the caller.

### Driver
| Method | Path | Handler / purpose |
|--------|------|-------------------|
| POST | `/driver/routes` | `CreateRouteHandler` — publish a route (async compute) |
| POST | `/driver/routes/{routeId}/activate` | `ActivateRouteHandler` |
| DELETE | `/driver/routes/{routeId}` | `DeleteRouteHandler` |
| GET | `/driver/requests` | **read-model** — incoming requests for my routes |
| POST | `/driver/requests/{requestId}/accept` | `AcceptRideRequestHandler` (row lock) |
| POST | `/driver/requests/{requestId}/reject` | `RejectRideRequestHandler` |
| GET | `/driver/rides` | **read-model** — my active rides as driver |
| POST | `/driver/rides/{rideId}/pickup` | `DeclareDriverPickupHandler` |
| POST | `/driver/rides/{rideId}/end` | `DeclareDriverEndHandler` |

### Passenger
| Method | Path | Handler / purpose |
|--------|------|-------------------|
| POST | `/passenger/routes/search` | `SubmitRouteSearchHandler` (async match) |
| POST | `/passenger/routes/{routeId}/requests` | `CreateRideRequestHandler` (quote+freeze) |
| GET | `/passenger/requests` | **read-model** — my ride requests + status |
| GET | `/passenger/rides` | **read-model** — my rides |
| POST | `/passenger/rides/{rideId}/pickup` | `DeclarePassengerPickupHandler` |
| POST | `/passenger/rides/{rideId}/end` | `DeclarePassengerEndHandler` |
| DELETE | `/passenger/rides/{rideId}` | `CancelRideHandler` (phase-dependent payment) |

> The four `GET` **read-model** endpoints (`{driver,passenger}/{requests,rides}`)
> back the frontend's my-rides and driver views. They return DTOs
> (`ReadModelDtos.cs`) read straight from the repositories — no command handler,
> no domain mutation.

## DB

PostGIS (tables: `routes`, `rides`, `ride_requests`, `route_jobs`, `service_area`).

`DbSession` holds a single `NpgsqlConnection` + optional `NpgsqlTransaction` and implements `IUnitOfWork`. Repositories take a `DbSession` by constructor injection — all repos in a single handler share one session and one transaction.

`EntityMapper` reconstructs domain objects from raw SQL rows using `RuntimeHelpers.GetUninitializedObject` + reflection, preserving private setters.

Pessimistic locking: `AcceptRideRequest` begins a transaction before reading the route and uses `SELECT … FOR UPDATE` so concurrent accepts serialize at the DB level.

## Comms

**RabbitMQ (publish):** `RabbitComputePublisher<ComputeJob>` serializes via `ComputeJobProtoMapper` to `ComputeMessage` protobuf and publishes to the exchange configured in `RabbitOptions`.

**RabbitMQ (consume):** `RabbitSubscriber` background service deserializes result bytes via `ComputeResultProtoMapper` into `IComputeResult` discriminated union (`DriverRouteComputeResult` / `PassengerMatchComputeResult` / `FailedComputeResult`) and dispatches to `RouteComputedHandler`.

**Kafka (publish):** `EventPublisher` wraps `IProducer<string, string>` and serializes `IDomainEvent` payloads to JSON (default `JsonSerializer` — PascalCase property names) into an envelope `{ EventType, EventId, OccurredAt, Payload }`.

**HTTP (inbound):** the gateway forwards REST over HTTP (YARP, `/api/route/**`
prefix stripped). Minimal-API endpoints map 1:1 to handlers — see below. (The
original design called for gRPC here; the implementation is REST.)

**External service interfaces** (all injected, all have `Fake*` implementations):
- `IAccountsService` — driver verification
- `IPaymentsService` — price quote+freeze, unfreeze, charge
- `IChatService` — create/close chat room
- `IGeoService` — service area check, proximity check

## Tests

| Project | Type | Collections |
|---------|------|-------------|
| `TripPlanner.UnitTests` | Unit | `Domain/` (Route, Ride, RideRequest state machines), `Features/` (all handlers via NSubstitute mocks) |
| `TripPlanner.IntegrationTests` | Integration (Testcontainers) | `Postgres` — repository round-trips + `SELECT FOR UPDATE` concurrency; `Queue` — RabbitMQ publisher; `Kafka` — event publisher; `Handlers` — full handler flows against real Postgres + mocked external services, including `AcceptRideRequestConcurrencyTests` |
