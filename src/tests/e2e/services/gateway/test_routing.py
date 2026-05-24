"""
Gateway routing tests -- verify that each upstream cluster is reachable through the gateway
and that path-prefix matching is correct.

Pass criteria:
  - 5xx from YARP itself (502/503/504) -> upstream is unreachable -> FAIL
  - Any 4xx/2xx from upstream -> upstream responded -> PASS for reachability tests

Requires: kubectl port-forward service/gateway 8080:80 -n pullapp
"""

import pytest
from uuid import uuid4

pytestmark = [pytest.mark.gateway, pytest.mark.cluster]

_GATEWAY_ERRORS = {502, 503, 504}


def _assert_reachable(resp, upstream: str) -> None:
    assert resp.status_code not in _GATEWAY_ERRORS, (
        f"{upstream} unreachable through gateway (HTTP {resp.status_code}). "
        "Verify the service is running: kubectl get pods -n pullapp"
    )


# -- path-prefix matching -----------------------------------------------------

async def test_unmapped_prefix_returns_404(gateway_client):
    resp = await gateway_client.get("/api/does-not-exist/anything")
    assert resp.status_code == 404


async def test_root_path_returns_404(gateway_client):
    resp = await gateway_client.get("/")
    assert resp.status_code == 404


async def test_health_prefix_not_proxied(gateway_client):
    # /health is served by the gateway itself, not forwarded upstream
    resp = await gateway_client.get("/health")
    assert resp.status_code == 200


# -- trip-planner cluster reachability ----------------------------------------

async def test_driver_prefix_reaches_trip_planner(gateway_client):
    # Send a valid request; 202 from trip-planner proves the upstream is reachable.
    resp = await gateway_client.post(
        "/api/driver/route",
        json={"start": {"lat": 52.2297, "lng": 21.0122}, "end": {"lat": 52.2500, "lng": 21.0300}},
        headers={"X-User-Id": str(uuid4())},
    )
    _assert_reachable(resp, "trip-planner")
    assert resp.status_code == 202, (
        f"Expected 202 from trip-planner -- got {resp.status_code}"
    )


async def test_passenger_prefix_reaches_trip_planner(gateway_client):
    resp = await gateway_client.post(
        "/api/passenger/route-requests",
        json={"start": {"lat": 52.2310, "lng": 21.0140}, "end": {"lat": 52.2480, "lng": 21.0280}},
        headers={"X-User-Id": str(uuid4())},
    )
    _assert_reachable(resp, "trip-planner")
    assert resp.status_code == 202, (
        f"Expected 202 from trip-planner -- got {resp.status_code}"
    )


async def test_x_user_id_header_forwarded_to_trip_planner(gateway_client):
    # Without X-User-Id trip-planner rejects the request (4xx).
    # Proves the header is what controls acceptance, not the gateway itself.
    resp = await gateway_client.post(
        "/api/driver/route",
        json={"start": {"lat": 52.2297, "lng": 21.0122}, "end": {"lat": 52.2500, "lng": 21.0300}},
    )
    _assert_reachable(resp, "trip-planner")
    assert resp.status_code in (400, 401, 404), (
        f"Expected a rejection (4xx) without X-User-Id -- got {resp.status_code}"
    )


# -- accounts cluster reachability --------------------------------------------

@pytest.mark.requires_accounts
async def test_auth_prefix_reaches_accounts(gateway_client):
    # Send an intentionally invalid body so accounts responds with 400/422,
    # proving the upstream is reachable without creating real data.
    resp = await gateway_client.post(
        "/api/auth/register",
        json={"email": "not-an-email", "password": "x"},
    )
    _assert_reachable(resp, "accounts")
    assert resp.status_code in (400, 422), (
        f"Expected 400/422 from accounts validation -- got {resp.status_code}"
    )
