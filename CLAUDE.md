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

Run from `src/infrastructure`:

```bash
./scripts/run-local.sh        # CI (act) + build images + load into minikube + deploy
./scripts/local-ci.sh         # CI only (runs GitHub Actions locally via act)
./scripts/local-cd.sh         # Deploy to minikube only

# Access the stack after deploy
kubectl port-forward service/gateway 8080:80 -n pullapp
```

Prerequisites: `act`, `minikube`, `kubectl`, `kustomize`, `docker`.

### Infrastructure (docker-compose for local dependencies)

```bash
docker compose -f src/infrastructure/docker-compose.yaml up -d
```

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

Both follow Clean Architecture: `Domain` → `Application` → `Infrastructure` → `Api`. The `Api` layer uses minimal APIs with endpoint classes implementing `IEndpoint`. Background services (e.g. RabbitMQ consumers) live in `TripPlanner.Api/BackgroundServices`.

### Kubernetes / deployment

Kustomize is used with a `base/` and `overlay/local/` structure under `src/infrastructure/k8s/`. Secrets are managed via `secretGenerator` with `PLACEHOLDER` values that must be replaced for real deployments. The `pullapp` namespace is always used.

### Shared schemas

Protobuf schemas shared across services live in `src/schemas/pullapp/core/v1/`. Each service that needs them generates its own language-specific output locally (not committed).

## Branching

GitFlow with pattern `type/service/description` (e.g. `feature/route-calc/autoscaling`, `hotfix/gateway/ssl`). Main branch is `master`. CI workflows are path-filtered — a push only triggers the CI for the service whose files changed.
