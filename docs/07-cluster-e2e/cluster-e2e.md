# 07 — Cluster E2E Testing

All tests live in `src/tests/e2e/`. They talk to live services over HTTP — no mocks, no in-process hosting, no source references. Every test is a black-box observer of observable side-effects: HTTP status codes and response bodies.

## Prerequisites

```bash
# Start the full stack
make run                 # from repo root — builds + deploys to minikube

# Expose the gateway
kubectl port-forward service/gateway 8080:80 -n pullapp
```

## Running gateway tests

```bash
# From src/tests/ — recommended
make test-gateway           # health + routing (all gateway tests)
make test-gateway-health    # /health, /health/live, /health/ready only
make test-gateway-routing   # upstream reachability + path-prefix matching

# Override the gateway address (e.g. different port or remote cluster)
make test-gateway BASE_URL=http://localhost:9090
```

Or run directly from `src/tests/e2e/`:

```bash
cd src/tests/e2e
pip install -r requirements.txt

# All gateway tests
pytest services/gateway/ -v

# Only health probes
pytest services/gateway/test_health.py -v

# Only routing / reachability
pytest services/gateway/test_routing.py -v
```

---

## Gateway tests

### Health probes (`services/gateway/test_health.py`)

These test the gateway's own health endpoints — no upstream involvement.

| Test | Endpoint | Expected |
|------|----------|----------|
| `test_gateway_health_returns_healthy` | `GET /health` | 200, `{"status":"healthy"}` |
| `test_gateway_liveness_probe` | `GET /health/live` | 200 |
| `test_gateway_readiness_probe` | `GET /health/ready` | 200 |
| `test_gateway_returns_404_for_unknown_path` | `GET /does-not-exist` | 404 |

All marked `gateway` + `cluster`.

---

### Routing and upstream reachability (`services/gateway/test_routing.py`)

These verify that the gateway's YARP routing table is correct and each upstream cluster responds.

**Pass criterion:** any 4xx from the upstream is a pass — it proves the upstream responded. A 502/503/504 means YARP could not reach the service and the test fails with a clear message.

| Test | What it proves |
|------|---------------|
| `test_unmapped_prefix_returns_404` | YARP returns 404 for paths not in the routing table |
| `test_root_path_returns_404` | `/` is not mapped (gateway has no index route) |
| `test_health_prefix_not_proxied` | `/health` is served locally, not forwarded upstream |
| `test_driver_prefix_reaches_trip_planner` | `/api/driver/**` → trip-planner cluster reachable; missing `X-User-Id` → 401 |
| `test_passenger_prefix_reaches_trip_planner` | `/api/passenger/**` → trip-planner cluster reachable; missing `X-User-Id` → 401 |
| `test_x_user_id_header_forwarded_to_trip_planner` | Gateway forwards `X-User-Id`; with header present, trip-planner does not return 401 |
| `test_auth_prefix_reaches_accounts` *(requires_accounts)* | `/api/auth/**` → accounts cluster reachable; invalid body → 400/422 |

#### Current routing table

Defined in `src/services/gateway/appsettings.json`:

| Path prefix | Upstream cluster |
|-------------|-----------------|
| `/api/auth/**` | accounts-cluster |
| `/api/driver/**` | trip-planner-cluster |
| `/api/passenger/**` | trip-planner-cluster |

#### Marker: `requires_accounts`

The accounts service is not yet deployed in the cluster. Tests marked `requires_accounts` are skipped by default. To run them once accounts is live:

```bash
pytest services/gateway/test_routing.py -m requires_accounts -v
```

---

## Test markers

| Marker | Meaning |
|--------|---------|
| `gateway` | Gateway-only tests; no upstream state required beyond reachability |
| `cluster` | Targets the full cluster through the gateway |
| `flow` | Multi-service orchestration flows |
| `requires_route_calc` | Needs route-calc running and consuming from RabbitMQ |
| `requires_accounts` | Needs the accounts service deployed |
| `dirty` | Not yet validated against the current cluster routing; excluded from `make test-gateway` |

Exclude dirty tests explicitly:

```bash
pytest -m "not dirty" -v
```

---

## Dirty tests (not yet validated)

The following test suites exist but are marked `dirty` — they were written against an older cluster configuration or depend on services not yet deployed. Do not run them as part of CI until they are re-validated.

| Suite | Location | Blocker |
|-------|----------|---------|
| accounts registration | `services/accounts/test_registration.py` | accounts not deployed |
| accounts login | `services/accounts/test_login.py` | accounts not deployed |
| trip-planner driver route | `services/trip_planner/test_driver_route.py` | needs re-validation |
| trip-planner driver confirmation | `services/trip_planner/test_driver_confirmation.py` | needs re-validation |
| trip-planner driver ride lifecycle | `services/trip_planner/test_driver_ride_lifecycle.py` | needs re-validation |
| trip-planner modify/cancel route | `services/trip_planner/test_modify_route.py` | needs re-validation |
| trip-planner passenger request | `services/trip_planner/test_passenger_request.py` | needs re-validation |
| trip-planner passenger ride | `services/trip_planner/test_passenger_ride.py` | needs re-validation |
| full ride flow | `flows/test_full_ride_flow.py` | needs re-validation |
| driver decline flow | `flows/test_driver_decline_flow.py` | needs re-validation |
| driver cancel/modify route flow | `flows/test_driver_cancel_route_flow.py` | needs re-validation |

To re-validate a suite: remove `pytest.mark.dirty` from its `pytestmark`, run it against a live cluster, fix any failures, then document it here.
