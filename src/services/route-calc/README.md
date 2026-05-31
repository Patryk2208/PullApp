# route-calc — Running Tests

## Prerequisites

- Docker
- `protoc` — `sudo apt install protobuf-compiler`

## Generate proto files

Run this once before running tests locally, and after any changes to `schemas/pullapp/core/v1/queue.proto`:

```bash
cd src/services/route-calc
mkdir -p route_calc/generated
protoc --proto_path=schemas/pullapp/core/v1/ \
       --python_out=route_calc/generated/ \
       schemas/pullapp/core/v1/queue.proto
touch route_calc/generated/__init__.py
```

## Environment

Copy the example env file and fill in credentials:

```bash
cp src/infrastructure/compose/env.example src/infrastructure/compose/.env
```

At minimum set `RABBITMQ_USER` and `RABBITMQ_PASS`.

## Running tests

From `src/infrastructure/compose/`:

```bash
# Integration tests
docker-compose \
  -f docker-compose.yml \
  -f docker-compose.messaging.yml \
  -f docker-compose.services.yml \
  --profile tests run --rm --build route-calc-tests

# E2E tests
docker-compose \
  -f docker-compose.yml \
  -f docker-compose.messaging.yml \
  -f docker-compose.services.yml \
  --profile e2e run --rm --build route-calc-e2e-tests
```
