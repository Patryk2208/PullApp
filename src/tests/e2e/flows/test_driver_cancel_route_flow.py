"""
Driver cancel/modify route flows.

Covers:
- Cancel before activation (Pending route → 404, no route-calc needed).
- Double register while Pending (documents allowed behaviour).
- Cancel after activation (requires route-calc).
- Cancel while passenger has match results → passenger notified via SSE.
- Modify route after activation (requires route-calc).

Service-level tests target trip-planner directly.
Tests marked @cluster run against the gateway.
"""

import pytest
from uuid import uuid4
from helpers import sse_open, sse_wait, register_route, create_passenger_request, parse_id

pytestmark = [pytest.mark.dirty, pytest.mark.flow]


# ── no route-calc needed ──────────────────────────────────────────────────────

async def test_cancel_before_activation_returns_404(trip_planner_client):
    driver_id = str(uuid4())

    route_resp = await register_route(trip_planner_client, driver_id)
    assert route_resp.status_code == 202

    cancel_resp = await trip_planner_client.delete(
        "/api/driver/route",
        headers={"X-User-Id": driver_id},
    )
    # Route is Pending (not Active) — CancelRoute returns 404.
    assert cancel_resp.status_code == 404


async def test_double_register_while_pending_is_accepted(trip_planner_client):
    # Documents that a second RegisterRoute succeeds while the first is still Pending
    # (GetActiveByDriverIdAsync only returns Active routes).
    driver_id = str(uuid4())

    r1 = await register_route(trip_planner_client, driver_id)
    assert r1.status_code == 202

    r2 = await register_route(trip_planner_client, driver_id)
    assert r2.status_code == 202


# ── requires route-calc ───────────────────────────────────────────────────────

@pytest.mark.requires_route_calc
async def test_cancel_route_after_activation_returns_204(trip_planner_client):
    driver_id = str(uuid4())
    driver_token = str(uuid4())

    driver_queue, driver_task = await sse_open(
        trip_planner_client,
        f"/api/driver/route/{driver_token}/events",
        headers={"X-User-Id": driver_id},
    )
    try:
        await register_route(trip_planner_client, driver_id)

        event, _ = await sse_wait(driver_queue, "drier_route_computed", timeout=30)
        assert event == "drier_route_computed", (
            "Timed out waiting for drier_route_computed — ensure route-calc is running"
        )
    finally:
        driver_task.cancel()

    cancel_resp = await trip_planner_client.delete(
        "/api/driver/route",
        headers={"X-User-Id": driver_id},
    )
    assert cancel_resp.status_code == 204


@pytest.mark.requires_route_calc
async def test_cancel_route_notifies_passenger_via_sse(trip_planner_client):
    driver_id = str(uuid4())
    passenger_id = str(uuid4())
    driver_token = str(uuid4())

    # Register and activate driver route.
    driver_queue, driver_task = await sse_open(
        trip_planner_client,
        f"/api/driver/route/{driver_token}/events",
        headers={"X-User-Id": driver_id},
    )
    try:
        await register_route(trip_planner_client, driver_id)

        event, _ = await sse_wait(driver_queue, "drier_route_computed", timeout=30)
        assert event == "drier_route_computed", (
            "Timed out waiting for drier_route_computed — ensure route-calc is running"
        )
    finally:
        driver_task.cancel()

    # Create passenger request.
    req_resp = await create_passenger_request(trip_planner_client, passenger_id)
    req_resp.raise_for_status()
    request_id = parse_id(req_resp, "requestId", "RequestId")

    # Open passenger SSE.
    passenger_queue, passenger_task = await sse_open(
        trip_planner_client,
        f"/api/passenger/route-requests/{request_id}/events",
        headers={"X-User-Id": passenger_id},
    )
    try:
        event, _ = await sse_wait(passenger_queue, "routes_ready", timeout=30)
        assert event == "routes_ready", "Timed out waiting for routes_ready"

        # Driver cancels route.
        cancel_resp = await trip_planner_client.delete(
            "/api/driver/route",
            headers={"X-User-Id": driver_id},
        )
        assert cancel_resp.status_code == 204

        # Passenger receives routes_expired or routes_ready (if other drivers remain).
        event, _ = await sse_wait(passenger_queue, "routes_expired", "routes_ready", timeout=5)
        assert event in ("routes_expired", "routes_ready"), (
            "Passenger was not notified via SSE after driver cancelled route"
        )
    finally:
        passenger_task.cancel()


@pytest.mark.requires_route_calc
async def test_modify_route_after_activation_returns_accepted(trip_planner_client):
    driver_id = str(uuid4())
    driver_token = str(uuid4())

    driver_queue, driver_task = await sse_open(
        trip_planner_client,
        f"/api/driver/route/{driver_token}/events",
        headers={"X-User-Id": driver_id},
    )
    try:
        await register_route(trip_planner_client, driver_id)

        event, _ = await sse_wait(driver_queue, "drier_route_computed", timeout=30)
        assert event == "drier_route_computed", (
            "Timed out waiting for drier_route_computed — ensure route-calc is running"
        )
    finally:
        driver_task.cancel()

    modify_resp = await trip_planner_client.put(
        "/api/driver/route",
        json={"start": {"lat": 52.2297, "lng": 21.0122}, "end": {"lat": 52.2600, "lng": 21.0450}},
        headers={"X-User-Id": driver_id},
    )
    assert modify_resp.status_code == 202
