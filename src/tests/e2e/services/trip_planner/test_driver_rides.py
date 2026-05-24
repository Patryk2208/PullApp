"""
Driver ride lifecycle — trip-planner via gateway.

POST /api/driver/requests/{requestId}/confirmation
POST /api/driver/rides/{rideId}/arrived
POST /api/driver/rides/{rideId}/start
POST /api/driver/rides/{rideId}/complete
POST /api/driver/rides/{rideId}/cancel

Sad-path tests (404 on unknown ID, 409 on wrong state) run without route-calc.
Happy-path state transitions require a Ride — covered in flows/.
"""

import pytest
from uuid import uuid4
from helpers import create_passenger_request, parse_id

pytestmark = pytest.mark.cluster

_USER_ID = lambda: str(uuid4())
_RIDE_ID = lambda: str(uuid4())


# -- confirmation -------------------------------------------------------------

async def test_confirm_request_404_when_not_found(gateway_client):
    resp = await gateway_client.post(
        f"/api/driver/requests/{uuid4()}/confirmation",
        json={"accepted": True},
        headers={"X-User-Id": _USER_ID()},
    )
    assert resp.status_code == 404


async def test_confirm_request_401_without_user_id(gateway_client):
    resp = await gateway_client.post(
        f"/api/driver/requests/{uuid4()}/confirmation",
        json={"accepted": True},
    )
    assert resp.status_code == 401


async def test_confirm_request_409_when_request_not_in_pending_driver_state(gateway_client):
    # A freshly created passenger request is in Searching state, not PendingDriver.
    passenger_id = _USER_ID()
    create_resp = await create_passenger_request(gateway_client, passenger_id)
    create_resp.raise_for_status()
    request_id = parse_id(create_resp, "requestId", "RequestId")

    resp = await gateway_client.post(
        f"/api/driver/requests/{request_id}/confirmation",
        json={"accepted": True},
        headers={"X-User-Id": _USER_ID()},
    )
    assert resp.status_code == 409


# -- arrived ------------------------------------------------------------------

async def test_arrived_404_when_ride_not_found(gateway_client):
    resp = await gateway_client.post(
        f"/api/driver/rides/{_RIDE_ID()}/arrived",
        headers={"X-User-Id": _USER_ID()},
    )
    assert resp.status_code == 404


async def test_arrived_401_without_user_id(gateway_client):
    resp = await gateway_client.post(f"/api/driver/rides/{_RIDE_ID()}/arrived")
    assert resp.status_code == 401


# -- start (driver) -----------------------------------------------------------

async def test_start_ride_driver_404_when_not_found(gateway_client):
    resp = await gateway_client.post(
        f"/api/driver/rides/{_RIDE_ID()}/start",
        headers={"X-User-Id": _USER_ID()},
    )
    assert resp.status_code == 404


async def test_start_ride_driver_401_without_user_id(gateway_client):
    resp = await gateway_client.post(f"/api/driver/rides/{_RIDE_ID()}/start")
    assert resp.status_code == 401


# -- complete -----------------------------------------------------------------

async def test_complete_ride_404_when_not_found(gateway_client):
    resp = await gateway_client.post(
        f"/api/driver/rides/{_RIDE_ID()}/complete",
        json={"dropoffPoint": {"lat": 52.2500, "lng": 21.0300}},
        headers={"X-User-Id": _USER_ID()},
    )
    assert resp.status_code == 404


async def test_complete_ride_401_without_user_id(gateway_client):
    resp = await gateway_client.post(
        f"/api/driver/rides/{_RIDE_ID()}/complete",
        json={"dropoffPoint": {"lat": 52.2500, "lng": 21.0300}},
    )
    assert resp.status_code == 401


# -- cancel (driver) ----------------------------------------------------------

async def test_cancel_ride_driver_404_when_not_found(gateway_client):
    resp = await gateway_client.post(
        f"/api/driver/rides/{_RIDE_ID()}/cancel",
        json={"reason": None},
        headers={"X-User-Id": _USER_ID()},
    )
    assert resp.status_code == 404


async def test_cancel_ride_driver_401_without_user_id(gateway_client):
    resp = await gateway_client.post(
        f"/api/driver/rides/{_RIDE_ID()}/cancel",
        json={"reason": None},
    )
    assert resp.status_code == 401
