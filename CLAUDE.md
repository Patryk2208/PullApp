# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

PullApp is a ride-sharing platform built as a monorepo of microservices. The services currently implemented are:

- **accounts** — .NET 10, user identity/auth, owns its PostgreSQL DB
- **trip-planner** — .NET 10, ride orchestration and state, owns PostGIS DB
- **route-calc** — Python 3.13 + C++20 (pybind11/OSRM), compute-heavy matching engine, KEDA-autoscaled
- **gateway** — .NET 10, API gateway (YARP), stateless
- **notifications** — Go, push notifications via Firebase, owns its PostgreSQL DB, consumes from Kafka
- **driver-tracker** — Go, real-time driver location tracking, owns its Redis instance

Services not yet implemented: chat (Go), payments (.NET), tile-server (Node.js/TileServer GL).

Frontend: React app at `src/frontend/pullapp-frontend/`.

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

Run from the repo root using the `Makefile`:

```bash
make start       # start minikube + install observability stack (first-time only)
make run         # build all images + load into minikube + deploy (idempotent)

make ci          # rebuild all service images and load into minikube
make cd          # deploy (kubectl apply + rollout wait)
make ci-gateway  # rebuild a single service image
make restart     # rolling restart all deployments (no rebuild)

make pf-gateway  # port-forward gateway → http://localhost:8080
make help        # full target list
```

Prerequisites: `act`, `minikube`, `kubectl`, `kustomize`, `helm`, `docker`.

### Infrastructure (compose — backing services only)

```bash
make infra           # start all backing services (db + cache + messaging)
make infra-db        # databases only
make infra-cache     # Redis only
make infra-messaging # RabbitMQ + Kafka only
make infra-down      # stop containers
make infra-clean     # stop + delete volumes (destructive)
```

Compose files live in `src/infrastructure/compose/` (split by concern: databases, cache, messaging).

### E2E tests (cluster-level)

Run from `src/tests/e2e` against a live cluster (gateway must be reachable):

```bash
# Setup
python -m venv .venv && source .venv/bin/activate
pip install -r requirements.txt

# Run all tests
pytest

# Run only full-cluster flow tests
pytest -m cluster

# Run a specific flow
pytest flows/test_full_ride_flow.py
```

Requires `BASE_URL` env var pointing at the gateway (default: `http://localhost:8080`). Tests tagged `requires_route_calc` need route-calc running and consuming from RabbitMQ.

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
