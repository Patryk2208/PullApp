"""
Driver decline flow: driver registers route → passenger requests → route-calc delivers
matches → passenger selects driver → driver declines → passenger receives match_declined.

Requires: gateway + route-calc running.
"""

import json
import pytest
from uuid import uuid4
from helpers import sse_open, sse_wait, register_route, create_passenger_request, parse_id

pytestmark = [pytest.mark.dirty, pytest.mark.flow, pytest.mark.cluster, pytest.mark.requires_route_calc]


async def test_driver_decline_sends_match_declined_to_passenger(gateway_client):
    driver_id = str(uuid4())
    passenger_id = str(uuid4())
    driver_token = str(uuid4())

    # ── driver SSE + register ─────────────────────────────────────────────────

    driver_queue, driver_task = await sse_open(
        gateway_client,
        f"/api/driver/route/{driver_token}/events",
        headers={"X-User-Id": driver_id},
    )
    try:
        await register_route(gateway_client, driver_id)

        event, _ = await sse_wait(driver_queue, "drier_route_computed", timeout=45)
        assert event == "drier_route_computed", (
            "Timed out waiting for drier_route_computed — ensure route-calc is running"
        )
    finally:
        driver_task.cancel()

    # ── passenger request + SSE ───────────────────────────────────────────────

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

        matches = json.loads(data).get("Matches") or json.loads(data).get("matches")
        assert matches and len(matches) > 0

        first = matches[0]
        driver_route_id = first.get("DriverRouteId") or first.get("driverRouteId")

        # ── passenger selects driver ──────────────────────────────────────────

        await gateway_client.post(
            f"/api/passenger/route-requests/{request_id}/select",
            json={"driverRouteId": driver_route_id},
            headers={"X-User-Id": passenger_id},
        )

        # ── driver declines ───────────────────────────────────────────────────

        decline_resp = await gateway_client.post(
            f"/api/driver/requests/{request_id}/confirmation",
            json={"accepted": False},
            headers={"X-User-Id": driver_id},
        )
        assert decline_resp.status_code == 204

        # ── passenger receives match_declined ─────────────────────────────────

        event, _ = await sse_wait(passenger_queue, "match_declined", timeout=5)
        assert event == "match_declined", "Passenger did not receive match_declined after driver declined"

    finally:
        passenger_task.cancel()
