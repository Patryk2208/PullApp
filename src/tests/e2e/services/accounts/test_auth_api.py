"""
Accounts service -- auth API tests.

POST /api/auth/register  (RegisterUserHandler)
POST /api/auth/login     (LoginUserHandler)

All requests go through the gateway (PULLAPP_GATEWAY_URL/api/auth/*).
Requires: accounts service deployed and gateway routing /api/auth/** to it.
"""

import pytest
import jwt
from uuid import uuid4
from datetime import date, timedelta

pytestmark = [pytest.mark.cluster, pytest.mark.requires_accounts]


# -- helpers ------------------------------------------------------------------

def _email() -> str:
    return f"test-{uuid4()}@pullapp-test.com"


def _adult_birthdate() -> str:
    return (date.today() - timedelta(days=365 * 20)).isoformat()


def _minor_birthdate() -> str:
    return (date.today() - timedelta(days=365 * 16)).isoformat()


def _valid_payload(**overrides) -> dict:
    return {
        "name": "Test",
        "surname": "User",
        "email": _email(),
        "password": "Test1234!",
        "birthDate": _adult_birthdate(),
        **overrides,
    }


def _extract_token(data: dict) -> str:
    return data.get("accessToken") or data.get("AccessToken", "")


# -- register -----------------------------------------------------------------

async def test_register_returns_201_with_user_id(gateway_client):
    resp = await gateway_client.post("/api/auth/register", json=_valid_payload())
    assert resp.status_code == 201
    data = resp.json()
    assert "userId" in data or "UserId" in data


async def test_register_400_when_name_missing(gateway_client):
    payload = _valid_payload()
    del payload["name"]
    resp = await gateway_client.post("/api/auth/register", json=payload)
    assert resp.status_code == 400


async def test_register_400_when_surname_missing(gateway_client):
    payload = _valid_payload()
    del payload["surname"]
    resp = await gateway_client.post("/api/auth/register", json=payload)
    assert resp.status_code == 400


async def test_register_400_when_email_invalid(gateway_client):
    resp = await gateway_client.post("/api/auth/register", json=_valid_payload(email="not-an-email"))
    assert resp.status_code == 400


async def test_register_400_when_password_too_short(gateway_client):
    resp = await gateway_client.post("/api/auth/register", json=_valid_payload(password="short"))
    assert resp.status_code == 400


async def test_register_400_when_user_is_underage(gateway_client):
    resp = await gateway_client.post(
        "/api/auth/register",
        json=_valid_payload(birthDate=_minor_birthdate()),
    )
    assert resp.status_code == 400


async def test_register_409_when_email_already_taken(gateway_client):
    email = _email()
    r1 = await gateway_client.post("/api/auth/register", json=_valid_payload(email=email))
    r1.raise_for_status()

    r2 = await gateway_client.post("/api/auth/register", json=_valid_payload(email=email))
    assert r2.status_code == 409


# -- login --------------------------------------------------------------------

async def test_login_returns_200_with_access_token(gateway_client):
    email, password = _email(), "Test1234!"
    reg = await gateway_client.post("/api/auth/register", json=_valid_payload(email=email, password=password))
    reg.raise_for_status()

    resp = await gateway_client.post("/api/auth/login", json={"email": email, "password": password})
    assert resp.status_code == 200
    token = _extract_token(resp.json())
    assert token, "expected a non-empty accessToken in the response body"


async def test_login_token_contains_sub_and_email_claims(gateway_client):
    email, password = _email(), "Test1234!"
    reg = await gateway_client.post("/api/auth/register", json=_valid_payload(email=email, password=password))
    reg.raise_for_status()

    resp = await gateway_client.post("/api/auth/login", json={"email": email, "password": password})
    resp.raise_for_status()

    # Decode without signature verification -- we only check structure, not trust
    claims = jwt.decode(_extract_token(resp.json()), options={"verify_signature": False})
    assert "sub" in claims, "token must carry a sub claim (userId)"
    assert claims.get("email") == email, "token must carry the user's email"


async def test_login_token_sub_matches_registered_user_id(gateway_client):
    email, password = _email(), "Test1234!"
    reg = await gateway_client.post("/api/auth/register", json=_valid_payload(email=email, password=password))
    reg.raise_for_status()
    user_id = str(reg.json().get("userId") or reg.json().get("UserId"))

    resp = await gateway_client.post("/api/auth/login", json={"email": email, "password": password})
    resp.raise_for_status()

    claims = jwt.decode(_extract_token(resp.json()), options={"verify_signature": False})
    assert claims["sub"] == user_id, "token sub must equal the userId returned at registration"


async def test_login_401_when_wrong_password(gateway_client):
    email = _email()
    reg = await gateway_client.post("/api/auth/register", json=_valid_payload(email=email, password="Correct1!"))
    reg.raise_for_status()

    resp = await gateway_client.post("/api/auth/login", json={"email": email, "password": "WrongPass1!"})
    assert resp.status_code == 401


async def test_login_401_when_user_not_found(gateway_client):
    resp = await gateway_client.post("/api/auth/login", json={"email": _email(), "password": "Test1234!"})
    assert resp.status_code == 401
