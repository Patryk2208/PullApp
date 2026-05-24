import os
import pytest
import httpx


class Endpoints:
    # Direct service URLs for service-level tests.
    # Defaults match docker-compose / local port-forward setups.
    trip_planner: str = os.getenv("PULLAPP_TRIP_PLANNER_URL", "http://localhost:5238")
    accounts: str = os.getenv("PULLAPP_ACCOUNTS_URL", "http://localhost:5100")

    # Gateway URL for full-cluster flow tests.
    # After: kubectl port-forward service/gateway 8080:80 -n pullapp
    gateway: str = os.getenv("PULLAPP_GATEWAY_URL", "http://localhost:8080")


@pytest.fixture
def endpoints() -> Endpoints:
    return Endpoints()


@pytest.fixture
async def trip_planner_client():
    async with httpx.AsyncClient(base_url=Endpoints.trip_planner, timeout=15.0) as client:
        yield client


@pytest.fixture
async def accounts_client():
    async with httpx.AsyncClient(base_url=Endpoints.accounts, timeout=15.0) as client:
        yield client


@pytest.fixture
async def gateway_client():
    async with httpx.AsyncClient(base_url=Endpoints.gateway, timeout=15.0) as client:
        yield client


@pytest.fixture
async def gateway_sse_client():
    timeout = httpx.Timeout(connect=10.0, read=60.0, write=10.0, pool=5.0)
    async with httpx.AsyncClient(base_url=Endpoints.gateway, timeout=timeout) as client:
        yield client
