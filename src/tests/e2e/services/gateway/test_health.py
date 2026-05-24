"""
Gateway own health endpoints.

Requires: kubectl port-forward service/gateway 8080:80 -n pullapp
"""

import pytest

pytestmark = [pytest.mark.gateway, pytest.mark.cluster]


async def test_gateway_health_returns_healthy(gateway_client):
    resp = await gateway_client.get("/health")
    assert resp.status_code == 200
    assert resp.json().get("status") == "healthy"


async def test_gateway_liveness_probe(gateway_client):
    resp = await gateway_client.get("/health/live")
    assert resp.status_code == 200


async def test_gateway_readiness_probe(gateway_client):
    resp = await gateway_client.get("/health/ready")
    assert resp.status_code == 200


async def test_gateway_returns_404_for_unknown_path(gateway_client):
    resp = await gateway_client.get("/does-not-exist")
    assert resp.status_code == 404
