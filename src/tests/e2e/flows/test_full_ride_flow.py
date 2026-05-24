"""
Full happy-path ride flow: driver registers route → passenger requests → passenger
selects driver → driver confirms → driver arrived → driver starts → driver completes.

flow tests run against the gateway (full cluster stack).
Set PULLAPP_GATEWAY_URL to override the target.

Partial flow (no route-calc) and full flow (requires route-calc) are both here.
"""

import json
import pytest
from uuid import uuid4
from helpers import sse_open, sse_wait, register_route, create_passenger_request, parse_id

pytestmark = [pytest.mark.dirty, pytest.mark.flow, pytest.mark.cluster]


async def test_register_route_and_passenger_request_allows_cancellation(gateway_client):
    """Partial flow — does not require route-calc."""
    driver_id = str(uuid4())
    passenger_id = str(uuid4())

    route_resp = await register_route(gateway_client, driver_id)
    assert route_resp.status_code == 202

    req_resp = await create_passenger_request(gateway_client, passenger_id)
    assert req_resp.status_code == 202
    request_id = parse_id(req_resp, "requestId", "RequestId")

    cancel_resp = await gateway_client.delete(
        f"/api/passenger/route-requests/{request_id}",
        headers={"X-User-Id": passenger_id},
    )
    assert cancel_resp.status_code == 204


@pytest.mark.requires_route_calc
async def test_full_ride_flow_completes(gateway_client):
    """
    Full 7-step flow requiring route-calc running in the cluster.

    1. Driver opens SSE, registers route → waits for drier_route_computed.
    2. Passenger creates request, opens SSE → waits for routes_ready.
    3. Passenger selects driver.
    4. Driver confirms → passenger receives match_confirmed with rideId.
    5. Driver marks arrived.
    6. Driver starts ride.
    7. Driver completes ride.
    """
    driver_id = str(uuid4())
    passenger_id = str(uuid4())
    driver_token = str(uuid4())

    # ── 1: driver SSE + register ──────────────────────────────────────────────

    driver_queue, driver_task = await sse_open(
        gateway_client,
        f"/api/driver/route/{driver_token}/events",
        headers={"X-User-Id": driver_id},
    )
    try:
        route_resp = await register_route(gateway_client, driver_id)
        assert route_resp.status_code == 202

        event, _ = await sse_wait(driver_queue, "drier_route_computed", timeout=45)
        assert event == "drier_route_computed", (
            "Timed out waiting for drier_route_computed — ensure route-calc is running"
        )
    finally:
        driver_task.cancel()

    # ── 2: passenger request + SSE ────────────────────────────────────────────

    req_resp = await create_passenger_request(gateway_client, passenger_id)
    req_resp.raise_for_status()
    request_id = parse_id(req_resp, "requestId", "RequestId")

    passenger_queue, passenger_task = await sse_open(
        gateway_client,
        f"/api/passenger/route-requests/{request_id}/events",
        headers={"X-User-Id": passenger_id},
    )
    try:
        event, data = await sse_wait(passenger_queue, "routes_ready", timeout=45)
        assert event == "routes_ready", "Timed out waiting for routes_ready"
        assert data

        matches = json.loads(data).get("Matches") or json.loads(data).get("matches")
        assert matches and len(matches) > 0, "routes_ready contained no matches"

        first = matches[0]
        driver_route_id = first.get("DriverRouteId") or first.get("driverRouteId")
        assert driver_route_id

        # ── 3: passenger selects driver ───────────────────────────────────────

        select_resp = await gateway_client.post(
            f"/api/passenger/route-requests/{request_id}/select",
            json={"driverRouteId": driver_route_id},
            headers={"X-User-Id": passenger_id},
        )
        assert select_resp.status_code == 204

        event, _ = await sse_wait(passenger_queue, "awaiting_driver", timeout=5)
        assert event == "awaiting_driver"

        # ── 4: driver confirms ────────────────────────────────────────────────

        confirm_resp = await gateway_client.post(
            f"/api/driver/requests/{request_id}/confirmation",
            json={"accepted": True},
            headers={"X-User-Id": driver_id},
        )
        assert confirm_resp.status_code == 204

        event, data = await sse_wait(passenger_queue, "match_confirmed", timeout=5)
        assert event == "match_confirmed"
        confirmed = json.loads(data)
        ride_id = confirmed.get("RideId") or confirmed.get("rideId")
        assert ride_id

        # ── 5: driver arrived ─────────────────────────────────────────────────

        arrived_resp = await gateway_client.post(
            f"/api/driver/rides/{ride_id}/arrived",
            headers={"X-User-Id": driver_id},
        )
        assert arrived_resp.status_code == 204

        # ── 6: driver starts ride ─────────────────────────────────────────────

        start_resp = await gateway_client.post(
            f"/api/driver/rides/{ride_id}/start",
            headers={"X-User-Id": driver_id},
        )
        assert start_resp.status_code == 204

        # ── 7: driver completes ride ──────────────────────────────────────────

        complete_resp = await gateway_client.post(
            f"/api/driver/rides/{ride_id}/complete",
            json={"dropoffPoint": {"lat": 52.2480, "lng": 21.0280}},
            headers={"X-User-Id": driver_id},
        )
        assert complete_resp.status_code == 204

    finally:
        passenger_task.cancel()
