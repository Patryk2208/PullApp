"""
Passenger route requests — trip-planner via gateway.

POST   /api/passenger/route-requests              Create request
GET    /api/passenger/route-requests/{id}/events  SSE stream (keyed by requestId)
POST   /api/passenger/route-requests/{id}/select  Select a matched route
DELETE /api/passenger/route-requests/{id}         Cancel request

SSE note: the stream is keyed by requestId (from the URL), not by X-User-Id.
SelectRoute requires the request to be in RoutesPresented state — calling it
in Searching state returns 409 (tested here without route-calc).
"""

import pytest
from uuid import uuid4
from helpers import sse_open, sse_wait, create_passenger_request, parse_id

pytestmark = pytest.mark.cluster

_WARSAW  = {"start": {"lat": 52.2297, "lng": 21.0122}, "end": {"lat": 52.2400, "lng": 21.0500}}
_OUTSIDE = {"start": {"lat": 51.5074, "lng": -0.1278}, "end": {"lat": 52.2400, "lng": 21.0500}}
_USER_ID = lambda: str(uuid4())


# -- create -------------------------------------------------------------------

async def test_create_request_returns_202_with_request_id(gateway_client):
    resp = await create_passenger_request(gateway_client, _USER_ID())
    assert resp.status_code == 202
    data = resp.json()
    assert "requestId" in data or "RequestId" in data


async def test_create_request_401_without_user_id(gateway_client):
    resp = await gateway_client.post("/api/passenger/route-requests", json=_WARSAW)
    assert resp.status_code == 401


async def test_create_request_409_when_passenger_already_has_active_request(gateway_client):
    passenger_id = _USER_ID()
    r1 = await create_passenger_request(gateway_client, passenger_id)
    r1.raise_for_status()
    r2 = await create_passenger_request(gateway_client, passenger_id)
    assert r2.status_code == 409


async def test_create_request_422_when_outside_service_area(gateway_client):
    resp = await gateway_client.post(
        "/api/passenger/route-requests",
        json=_OUTSIDE,
        headers={"X-User-Id": _USER_ID()},
    )
    assert resp.status_code == 422


async def test_create_request_with_constraints(gateway_client):
    resp = await gateway_client.post(
        "/api/passenger/route-requests",
        json={**_WARSAW, "constraints": {"maxDetourKm": 3, "maxResults": 3}},
        headers={"X-User-Id": _USER_ID()},
    )
    assert resp.status_code == 202


# -- SSE stream ---------------------------------------------------------------

async def test_passenger_sse_stream_opens(gateway_sse_client):
    request_id = str(uuid4())
    queue, task = await sse_open(
        gateway_sse_client,
        f"/api/passenger/route-requests/{request_id}/events",
        headers={"X-User-Id": _USER_ID()},
    )
    try:
        assert queue is not None
    finally:
        task.cancel()


@pytest.mark.requires_route_calc
async def test_passenger_sse_receives_routes_ready_event(gateway_client):
    from helpers import register_route
    driver_id = _USER_ID()
    passenger_id = _USER_ID()

    # Driver registers first
    driver_queue, driver_task = await sse_open(
        gateway_client,
        f"/api/driver/route/{uuid4()}/events",
        headers={"X-User-Id": driver_id},
    )
    try:
        await register_route(gateway_client, driver_id)
        await sse_wait(driver_queue, "drier_route_computed", timeout=30)
    finally:
        driver_task.cancel()

    # Passenger creates request and waits for matches
    create_resp = await create_passenger_request(gateway_client, passenger_id)
    create_resp.raise_for_status()
    request_id = parse_id(create_resp, "requestId", "RequestId")

    pax_queue, pax_task = await sse_open(
        gateway_client,
        f"/api/passenger/route-requests/{request_id}/events",
        headers={"X-User-Id": passenger_id},
    )
    try:
        event, data = await sse_wait(pax_queue, "routes_ready", timeout=30)
        assert event == "routes_ready"
        assert data
    finally:
        pax_task.cancel()


# -- select -------------------------------------------------------------------

async def test_select_route_404_when_request_not_found(gateway_client):
    resp = await gateway_client.post(
        f"/api/passenger/route-requests/{uuid4()}/select",
        json={"driverRouteId": str(uuid4())},
        headers={"X-User-Id": _USER_ID()},
    )
    assert resp.status_code == 404


async def test_select_route_401_without_user_id(gateway_client):
    resp = await gateway_client.post(
        f"/api/passenger/route-requests/{uuid4()}/select",
        json={"driverRouteId": str(uuid4())},
    )
    assert resp.status_code == 401


async def test_select_route_403_when_not_owner(gateway_client):
    owner_id = _USER_ID()
    create_resp = await create_passenger_request(gateway_client, owner_id)
    create_resp.raise_for_status()
    request_id = parse_id(create_resp, "requestId", "RequestId")

    resp = await gateway_client.post(
        f"/api/passenger/route-requests/{request_id}/select",
        json={"driverRouteId": str(uuid4())},
        headers={"X-User-Id": _USER_ID()},
    )
    assert resp.status_code == 403


async def test_select_route_409_when_request_not_in_routes_presented_state(gateway_client):
    # Freshly created request is in Searching state, not RoutesPresented.
    passenger_id = _USER_ID()
    create_resp = await create_passenger_request(gateway_client, passenger_id)
    create_resp.raise_for_status()
    request_id = parse_id(create_resp, "requestId", "RequestId")

    resp = await gateway_client.post(
        f"/api/passenger/route-requests/{request_id}/select",
        json={"driverRouteId": str(uuid4())},
        headers={"X-User-Id": passenger_id},
    )
    assert resp.status_code == 409


# -- cancel -------------------------------------------------------------------

async def test_cancel_request_204(gateway_client):
    passenger_id = _USER_ID()
    create_resp = await create_passenger_request(gateway_client, passenger_id)
    create_resp.raise_for_status()
    request_id = parse_id(create_resp, "requestId", "RequestId")

    resp = await gateway_client.delete(
        f"/api/passenger/route-requests/{request_id}",
        headers={"X-User-Id": passenger_id},
    )
    assert resp.status_code == 204


async def test_cancel_request_404_when_not_found(gateway_client):
    resp = await gateway_client.delete(
        f"/api/passenger/route-requests/{uuid4()}",
        headers={"X-User-Id": _USER_ID()},
    )
    assert resp.status_code == 404


async def test_cancel_request_401_without_user_id(gateway_client):
    resp = await gateway_client.delete(f"/api/passenger/route-requests/{uuid4()}")
    assert resp.status_code == 401


async def test_cancel_request_403_when_not_owner(gateway_client):
    owner_id = _USER_ID()
    create_resp = await create_passenger_request(gateway_client, owner_id)
    create_resp.raise_for_status()
    request_id = parse_id(create_resp, "requestId", "RequestId")

    resp = await gateway_client.delete(
        f"/api/passenger/route-requests/{request_id}",
        headers={"X-User-Id": _USER_ID()},
    )
    assert resp.status_code == 403
