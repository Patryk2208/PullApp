"""
Passenger ride actions — trip-planner via gateway.

POST /api/passenger/rides/{rideId}/start
POST /api/passenger/rides/{rideId}/confirm-price
POST /api/passenger/rides/{rideId}/cancel

Sad-path tests (404 on unknown ID, 401) run without route-calc.
Happy-path and 403 ownership tests require a Ride — covered in flows/.
"""

import pytest
from uuid import uuid4

pytestmark = pytest.mark.cluster

_USER_ID = lambda: str(uuid4())
_RIDE_ID = lambda: str(uuid4())


# -- start (passenger) --------------------------------------------------------

async def test_passenger_start_ride_404_when_not_found(gateway_client):
    resp = await gateway_client.post(
        f"/api/passenger/rides/{_RIDE_ID()}/start",
        headers={"X-User-Id": _USER_ID()},
    )
    assert resp.status_code == 404


async def test_passenger_start_ride_401_without_user_id(gateway_client):
    resp = await gateway_client.post(f"/api/passenger/rides/{_RIDE_ID()}/start")
    assert resp.status_code == 401


# -- confirm-price ------------------------------------------------------------

async def test_confirm_price_404_when_not_found(gateway_client):
    resp = await gateway_client.post(
        f"/api/passenger/rides/{_RIDE_ID()}/confirm-price",
        headers={"X-User-Id": _USER_ID()},
    )
    assert resp.status_code == 404


async def test_confirm_price_401_without_user_id(gateway_client):
    resp = await gateway_client.post(f"/api/passenger/rides/{_RIDE_ID()}/confirm-price")
    assert resp.status_code == 401


# -- cancel (passenger) -------------------------------------------------------

async def test_passenger_cancel_ride_404_when_not_found(gateway_client):
    resp = await gateway_client.post(
        f"/api/passenger/rides/{_RIDE_ID()}/cancel",
        json={"reason": None},
        headers={"X-User-Id": _USER_ID()},
    )
    assert resp.status_code == 404


async def test_passenger_cancel_ride_401_without_user_id(gateway_client):
    resp = await gateway_client.post(
        f"/api/passenger/rides/{_RIDE_ID()}/cancel",
        json={"reason": None},
    )
    assert resp.status_code == 401
