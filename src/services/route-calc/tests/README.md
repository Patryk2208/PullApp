# Route-calc RabbitMQ tests

Docker-based tests for RabbitMQ connectivity and the full message path through `route-calc` (including C++).

**Prerequisites:** Docker, docker-compose, and a `.env` in `src/infrastructure/` with at least `RABBITMQ_USER` and `RABBITMQ_PASS` (see `.env.example`).

Run everything from `src/infrastructure/`:

```bash
cd src/infrastructure

# RabbitMQ integration (no route-calc worker)
docker-compose --profile tests run --rm route-calc-tests

# End-to-end: route-calc consumer + C++ ride matching
docker-compose --profile e2e run --rm route-calc-e2e-tests
```

The **`tests`** profile only starts RabbitMQ. The **`e2e`** profile also starts `queue-setup` (purges queues), the `route-calc` service, then runs the e2e test.

If integration tests behave oddly while `route-calc` is still running from a previous e2e run:

```bash
docker-compose --profile e2e stop route-calc
```

---

## `rabbitmq_integration_test.py` (7 tests)

Checks that the test harness can talk to RabbitMQ and publish job-shaped JSON messages. Does **not** run the route-calc worker or C++ code.

| Test | What it checks |
|------|----------------|
| `test_rabbitmq_connection` | Connect to RabbitMQ with configured credentials. |
| `test_queue_declaration` | Declare `compute-queue` and `results-queue`. |
| `test_publish_best_route_job` | Publish a `best_route` JSON job to `compute-queue`; verify delivery on an isolated queue. |
| `test_publish_ride_matching_job` | Publish a `ride_matching` JSON job to `compute-queue`. |
| `test_publish_closest_routes_job` | Publish a `closest_routes` JSON job to `compute-queue`. |
| `test_message_roundtrip` | Publish and receive a message on a temporary `test-roundtrip` queue. |
| `test_multiple_jobs_batched` | Publish several jobs in a row; verify batch delivery on an isolated queue. |

---

## `rabbitmq_e2e_test.py` (1 test)

| Test | What it checks |
|------|----------------|
| `test_ride_matching_through_rabbitmq_and_cpp` | Purges queues, publishes a **protobuf** `ride_matching` job (Warsaw → Kraków) to `compute-queue`, waits for a **protobuf** result on `results-queue`, and asserts the C++ `match_single_route` output (match score, pickup/dropoff indices). |

This is the only test that exercises the real consumer and compiled algorithms.

---

## Running locally (optional)

With RabbitMQ reachable on `localhost:5672` and dependencies installed (`poetry install` in `src/services/route-calc/`):

```bash
pytest tests/rabbitmq_integration_test.py -v
# e2e also requires route-calc running and correct env vars (RABBITMQ_HOST, etc.)
pytest tests/rabbitmq_e2e_test.py -v
```
