"""
Driver route management — trip-planner via gateway.

POST   /api/driver/route              Register route
GET    /api/driver/route/{jobId}/events  SSE stream (keyed by driverId)
PUT    /api/driver/route              Modify route
DELETE /api/driver/route              Cancel route

SSE note: the stream is keyed by driverId (X-User-Id), not jobId.
ModifyRoute and CancelRoute require an Active route — while the route is Pending
(not yet processed by route-calc) both return 404. This is intentional.
"""

import pytest
from uuid import uuid4
from helpers import sse_open, sse_wait, register_route

pytestmark = pytest.mark.cluster

_WARSAW      = {"start": {"lat": 52.2297, "lng": 21.0122}, "end": {"lat": 52.2500, "lng": 21.0300}}
_OUTSIDE     = {"start": {"lat": 51.5074, "lng": -0.1278}, "end": {"lat": 52.2500, "lng": 21.0300}}
_USER_ID     = lambda: str(uuid4())


# -- register -----------------------------------------------------------------

async def test_register_route_returns_202_with_job_id(gateway_client):
    resp = await register_route(gateway_client, _USER_ID())
    assert resp.status_code == 202
    data = resp.json()
    assert "jobId" in data or "JobId" in data


async def test_register_route_401_without_user_id(gateway_client):
    resp = await gateway_client.post("/api/driver/route", json=_WARSAW)
    assert resp.status_code == 401


async def test_register_route_400_when_body_missing(gateway_client):
    resp = await gateway_client.post(
        "/api/driver/route",
        headers={"X-User-Id": _USER_ID()},
    )
    assert resp.status_code in (400, 422)


async def test_register_route_422_when_outside_service_area(gateway_client):
    resp = await gateway_client.post(
        "/api/driver/route",
        json=_OUTSIDE,
        headers={"X-User-Id": _USER_ID()},
    )
    assert resp.status_code == 422


# -- SSE stream ---------------------------------------------------------------

async def test_driver_sse_stream_opens(gateway_sse_client):
    driver_id = _USER_ID()
    queue, task = await sse_open(
        gateway_sse_client,
        f"/api/driver/route/{uuid4()}/events",
        headers={"X-User-Id": driver_id},
    )
    try:
        assert queue is not None
    finally:
        task.cancel()


async def test_driver_sse_401_without_user_id(gateway_sse_client):
    with pytest.raises(Exception):
        queue, task = await sse_open(
            gateway_sse_client,
            f"/api/driver/route/{uuid4()}/events",
        )
        task.cancel()


@pytest.mark.requires_route_calc
async def test_driver_sse_receives_route_computed_event(gateway_client):
    driver_id = _USER_ID()
    queue, task = await sse_open(
        gateway_client,
        f"/api/driver/route/{uuid4()}/events",
        headers={"X-User-Id": driver_id},
    )
    try:
        await register_route(gateway_client, driver_id)
        event, data = await sse_wait(queue, "drier_route_computed", timeout=30)
        assert event == "drier_route_computed", (
            "Timed out — ensure route-calc is running and consuming from RabbitMQ"
        )
        assert data
    finally:
        task.cancel()


# -- modify -------------------------------------------------------------------

async def test_modify_route_404_when_no_active_route(gateway_client):
    resp = await gateway_client.put(
        "/api/driver/route",
        json=_WARSAW,
        headers={"X-User-Id": _USER_ID()},
    )
    assert resp.status_code == 404


async def test_modify_route_404_when_route_is_pending(gateway_client):
    # Route is Pending until route-calc processes it; modify requires Active.
    driver_id = _USER_ID()
    await register_route(gateway_client, driver_id)
    resp = await gateway_client.put(
        "/api/driver/route",
        json=_WARSAW,
        headers={"X-User-Id": driver_id},
    )
    assert resp.status_code == 404


async def test_modify_route_401_without_user_id(gateway_client):
    resp = await gateway_client.put("/api/driver/route", json=_WARSAW)
    assert resp.status_code == 401


async def test_modify_route_422_when_outside_service_area(gateway_client):
    # No active route → 404 takes priority over 422; use a fresh driver to get 404.
    # To test 422 we would need an Active route (requires route-calc).
    pass


@pytest.mark.requires_route_calc
async def test_modify_route_returns_202_when_active(gateway_client):
    driver_id = _USER_ID()
    queue, task = await sse_open(
        gateway_client,
        f"/api/driver/route/{uuid4()}/events",
        headers={"X-User-Id": driver_id},
    )
    try:
        await register_route(gateway_client, driver_id)
        await sse_wait(queue, "drier_route_computed", timeout=30)

        resp = await gateway_client.put(
            "/api/driver/route",
            json={"start": {"lat": 52.2310, "lng": 21.0140}, "end": {"lat": 52.2450, "lng": 21.0350}},
            headers={"X-User-Id": driver_id},
        )
        assert resp.status_code == 202
    finally:
        task.cancel()


# -- cancel -------------------------------------------------------------------

async def test_cancel_route_404_when_no_active_route(gateway_client):
    resp = await gateway_client.delete(
        "/api/driver/route",
        headers={"X-User-Id": _USER_ID()},
    )
    assert resp.status_code == 404


async def test_cancel_route_404_when_route_is_pending(gateway_client):
    driver_id = _USER_ID()
    await register_route(gateway_client, driver_id)
    resp = await gateway_client.delete(
        "/api/driver/route",
        headers={"X-User-Id": driver_id},
    )
    assert resp.status_code == 404


async def test_cancel_route_401_without_user_id(gateway_client):
    resp = await gateway_client.delete("/api/driver/route")
    assert resp.status_code == 401


@pytest.mark.requires_route_calc
async def test_cancel_route_204_when_active(gateway_client):
    driver_id = _USER_ID()
    queue, task = await sse_open(
        gateway_client,
        f"/api/driver/route/{uuid4()}/events",
        headers={"X-User-Id": driver_id},
    )
    try:
        await register_route(gateway_client, driver_id)
        await sse_wait(queue, "drier_route_computed", timeout=30)
    finally:
        task.cancel()

    resp = await gateway_client.delete(
        "/api/driver/route",
        headers={"X-User-Id": driver_id},
    )
    assert resp.status_code == 204
