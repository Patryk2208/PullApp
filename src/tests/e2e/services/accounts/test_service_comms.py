"""
Accounts service -- service-to-service communication tests.

Current state: accounts has NO outbound integrations.
  - No calls to other services
  - No Kafka / RabbitMQ publishing
  - No Redis

Planned integrations (tests below are skipped until implemented):
  - Gateway JWT forwarding: token issued by accounts accepted by trip-planner
  - Token passed as Authorization: Bearer through the gateway without stripping
"""

import pytest
import jwt

pytestmark = [pytest.mark.cluster, pytest.mark.requires_accounts]


# -- helpers ------------------------------------------------------------------

def _extract_token(data: dict) -> str:
    return data.get("accessToken") or data.get("AccessToken", "")


async def _register_and_login(client) -> str:
    """Returns a valid access token for a freshly registered user."""
    from uuid import uuid4
    from datetime import date, timedelta
    email = f"test-{uuid4()}@pullapp-test.com"
    password = "Test1234!"
    payload = {
        "name": "Test",
        "surname": "User",
        "email": email,
        "password": password,
        "birthDate": (date.today() - timedelta(days=365 * 20)).isoformat(),
    }
    reg = await client.post("/api/auth/register", json=payload)
    reg.raise_for_status()
    login = await client.post("/api/auth/login", json={"email": email, "password": password})
    login.raise_for_status()
    return _extract_token(login.json())


# -- gateway JWT forwarding ---------------------------------------------------
# These tests verify that the gateway passes Authorization headers through
# to upstream services without stripping or modifying them.
# Blocked on: trip-planner validating JWT Bearer tokens (not yet implemented --
# it currently relies on X-User-Id instead of JWT auth).

@pytest.mark.skip(reason="pending: trip-planner does not yet validate JWT bearer tokens")
async def test_token_forwarded_through_gateway_to_trip_planner(gateway_client):
    """
    register -> login -> use token as Authorization: Bearer on a trip-planner endpoint.
    Proves the gateway does not strip the Authorization header and trip-planner
    accepts it as identity proof (replacing X-User-Id).
    """
    token = await _register_and_login(gateway_client)
    claims = jwt.decode(token, options={"verify_signature": False})
    user_id = claims["sub"]

    resp = await gateway_client.post(
        "/api/driver/route",
        json={"start": {"lat": 52.2297, "lng": 21.0122}, "end": {"lat": 52.2500, "lng": 21.0300}},
        headers={"Authorization": f"Bearer {token}"},
    )
    # Once JWT auth is wired, expect 202 (accepted) not 401
    assert resp.status_code == 202, (
        f"Expected 202 -- trip-planner may not yet accept JWT auth (got {resp.status_code})"
    )
    _ = user_id  # suppress unused warning; sub is what trip-planner should extract


@pytest.mark.skip(reason="pending: trip-planner does not yet validate JWT bearer tokens")
async def test_expired_token_rejected_by_trip_planner(gateway_client):
    """
    Verifies the full auth chain rejects a tampered/expired token rather than
    falling back to an unauthenticated request.
    """
    resp = await gateway_client.post(
        "/api/driver/route",
        json={"start": {"lat": 52.2297, "lng": 21.0122}, "end": {"lat": 52.2500, "lng": 21.0300}},
        headers={"Authorization": "Bearer this.is.invalid"},
    )
    assert resp.status_code == 401
