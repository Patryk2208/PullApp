# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

PullApp is a ride-sharing platform built as a monorepo of microservices. The services currently implemented are:

- **accounts** — .NET 10, user identity/auth, owns its PostgreSQL DB
- **trip-planner** — .NET 10, ride orchestration and state, owns PostGIS DB
- **route-calc** — Python 3.13 + C++20 (pybind11/OSRM), compute-heavy matching engine, KEDA-autoscaled
- **gateway** — .NET 10, API gateway (YARP), stateless

Services not yet implemented: chat (Go), notifications (Go), payments (.NET), tile-server (Node.js/TileServer GL), driver-tracker (Go).

## Commands

### .NET services (accounts, trip-planner, gateway)

Run from the service directory (e.g. `src/services/accounts`):

```bash
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
dotnet test --no-build --configuration Release  # skip rebuild
```

#### trip-planner test projects

trip-planner has two test projects. Run from `src/services/trip-planner`:

```bash
# Unit tests (domain model + handler logic via mocks, no containers)
dotnet test tests/TripPlanner.UnitTests --configuration Release

# Integration tests (Testcontainers: PostGIS, RabbitMQ, Kafka)
dotnet test tests/TripPlanner.IntegrationTests --configuration Release

# Integration tests — specific collection only
dotnet test tests/TripPlanner.IntegrationTests --configuration Release --filter "FullyQualifiedName~Postgres"
dotnet test tests/TripPlanner.IntegrationTests --configuration Release --filter "FullyQualifiedName~Handlers"
dotnet test tests/TripPlanner.IntegrationTests --configuration Release --filter "FullyQualifiedName~Queue"
dotnet test tests/TripPlanner.IntegrationTests --configuration Release --filter "FullyQualifiedName~Kafka"
```

### route-calc (Python + C++)

Run from `src/services/route-calc`:

```bash
# Setup
poetry install

# Protobuf codegen (required before running/testing)
mkdir -p route_calc/generated
protoc --proto_path=../../schemas/pullapp/core/v1/ \
       --python_out=route_calc/generated/ \
       ../../schemas/pullapp/core/v1/queue.proto

# Run all tests
poetry run pytest

# Run a single test file
poetry run pytest tests/autoscaling_test.py

# Run a single test
poetry run pytest tests/autoscaling_test.py::test_name
```

### Local full-stack (Kubernetes via minikube)

All cluster/build/deploy orchestration lives in the **top-level `Makefile`** — run
from the repo root. `make help` lists every target. Key ones:

```bash
make start          # First-time setup: start minikube + install obs stack + KEDA
make run            # Full from scratch: cluster + obs + infra + build/load + deploy
make build          # Build all service images (make build-<svc> for one)
make ci             # Build all, load into minikube, restart deployments
make ci-<svc>       # CI for a single service (e.g. make ci-notifications)
make cd             # kubectl apply kustomize overlay + wait for rollouts
make ci-full        # Run all GitHub Actions workflows locally via act
make pf-gateway     # Port-forward gateway → http://localhost:8080
make status         # Cluster + pod + compose summary
```

Prerequisites: `act`, `minikube`, `kubectl`, `kustomize`, `helm`, `docker`.

### Infrastructure (docker-compose for local dependencies)

Compose deps (Postgres, Redis, RabbitMQ, Kafka) are also driven from the
top-level `Makefile`:

```bash
make infra          # Start all local deps (db, cache, messaging)
make infra-db       # Databases only
make infra-cache    # Caches only
make infra-messaging # Messaging only
make infra-down     # Stop and remove
```

Compose files live in `src/infrastructure/compose/` (`docker-compose.yml` plus
`databases`, `cache`, `messaging` overlays).

## Architecture

### Communication topology

- Mobile clients → **gateway** (HTTPS/WebSocket)
- Gateway → trip-planner, accounts, payments via **gRPC**
- Gateway → chat, driver-tracker via **WebSocket**
- Trip-planner → route-calc via **RabbitMQ** (AMQP), results returned via **gRPC**
- All services → **Redis** for caching/pub-sub
- Notifications consumed from **Kafka**

### route-calc internals

The service has two layers: a Python layer (FastAPI + aio-pika consumer) and a C++ layer (OSRM routing) exposed to Python via pybind11. The C++ module is built using scikit-build-core. Proto-generated code lives in `route_calc/generated/` and must be regenerated from `src/schemas/pullapp/core/v1/queue.proto` whenever the schema changes. The service is KEDA-autoscaled on RabbitMQ queue depth — see `src/infrastructure/k8s/base/services/route-calc/scaled-object.yaml`.

### .NET services (accounts, trip-planner)

Both follow Clean Architecture: `Domain` → `Application` → `Infrastructure` → `Api`. The `Api` layer uses minimal APIs with endpoint classes implementing `IEndpoint`. Background services (e.g. the RabbitMQ result consumer) live in `TripPlanner.Api/BackgroundServices`.

trip-planner owns three DDD aggregates: `Route` (Calculating→Created→Active→Full), `Ride` (WaitingForActivation→WaitingForDriver→Started), and `RideRequest` (Pending→Accepted/Rejected). All DB access goes through a `DbSession` that implements `IUnitOfWork`; pessimistic row locking (`SELECT … FOR UPDATE`) is used in `AcceptRideRequest` to prevent double-booking. External services (Accounts, Payments, Chat, Kafka, RabbitMQ) are injected as interfaces and have `Fake*` implementations for local development.

### Kubernetes / deployment

Kustomize is used with a `base/` and `overlay/local/` structure under `src/infrastructure/k8s/`. Secrets are managed via `secretGenerator` with `PLACEHOLDER` values that must be replaced for real deployments. The `pullapp` namespace is always used.

### Shared schemas

Protobuf schemas shared across services live in `src/schemas/pullapp/core/v1/`. Each service that needs them generates its own language-specific output locally (not committed).

## Branching

GitFlow with pattern `type/service/description` (e.g. `feature/route-calc/autoscaling`, `hotfix/gateway/ssl`). Main branch is `master`. CI workflows are path-filtered — a push only triggers the CI for the service whose files changed.

## Commit style

Short lowercase phrases, no period, no `Co-Authored-By` or other trailers. Examples: `docs updated`, `ci update`, `all unit tests pass`.
