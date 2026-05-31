"""
Integration test for RouteDB — spins up a real PostGIS container.

Requires Docker. Run with:
  poetry run pytest tests/db_test.py -v
"""
import os
import uuid

import psycopg
import pytest
from testcontainers.postgres import PostgresContainer

# Ryuk (the container reaper) can fail in rootless-Docker / CI environments.
os.environ.setdefault("TESTCONTAINERS_RYUK_DISABLED", "true")

from route_calc.infra.db import RouteDB

_IMAGE = "postgis/postgis:17-3.5"

_SCHEMA = """
CREATE EXTENSION IF NOT EXISTS postgis;

CREATE TABLE IF NOT EXISTS routes (
    id                   UUID        PRIMARY KEY,
    driver_id            UUID        NOT NULL,
    status               TEXT        NOT NULL,
    start_lat            FLOAT8      NOT NULL,
    start_lng            FLOAT8      NOT NULL,
    end_lat              FLOAT8      NOT NULL,
    end_lng              FLOAT8      NOT NULL,
    current_location_lat FLOAT8,
    current_location_lng FLOAT8,
    capacity             INTEGER     NOT NULL,
    active_ride_count    INTEGER     NOT NULL DEFAULT 0,
    route_geom           geometry(LineString, 4326),
    duration_seconds     FLOAT8,
    distance_meters      FLOAT8,
    created_at           TIMESTAMPTZ NOT NULL,
    activated_at         TIMESTAMPTZ
);
"""


def _insert_route(conn, *, route_id, driver_id, status, capacity, active_ride_count, wkt=None):
    if wkt is not None:
        conn.execute(
            """
            INSERT INTO routes
                (id, driver_id, status, start_lat, start_lng, end_lat, end_lng,
                 capacity, active_ride_count, route_geom, created_at)
            VALUES (%s, %s, %s, 0, 0, 1, 1, %s, %s, ST_GeomFromText(%s, 4326), now())
            """,
            (route_id, driver_id, status, capacity, active_ride_count, wkt),
        )
    else:
        conn.execute(
            """
            INSERT INTO routes
                (id, driver_id, status, start_lat, start_lng, end_lat, end_lng,
                 capacity, active_ride_count, created_at)
            VALUES (%s, %s, %s, 0, 0, 1, 1, %s, %s, now())
            """,
            (route_id, driver_id, status, capacity, active_ride_count),
        )


def _conninfo(container: PostgresContainer) -> str:
    return (
        f"host={container.get_container_host_ip()} "
        f"port={container.get_exposed_port(5432)} "
        f"dbname={container.dbname} "
        f"user={container.username} "
        f"password={container.password}"
    )


def _make_config(container: PostgresContainer) -> dict:
    return {
        "host":     container.get_container_host_ip(),
        "port":     container.get_exposed_port(5432),
        "database": container.dbname,
        "username": container.username,
        "password": container.password,
        "pool":     {"min_connections": 1, "max_connections": 4},
    }


@pytest.fixture(scope="module")
def pg():
    with PostgresContainer(_IMAGE) as container:
        with psycopg.connect(_conninfo(container)) as conn:
            conn.autocommit = True
            conn.execute(_SCHEMA)
        yield container


def test_get_active_routes_returns_seeded_route(pg):
    route_id  = uuid.uuid4()
    driver_id = uuid.uuid4()
    wkt = "LINESTRING(21.0 52.2, 21.1 52.3)"

    with psycopg.connect(_conninfo(pg)) as conn:
        _insert_route(conn, route_id=route_id, driver_id=driver_id,
                      status="Active", capacity=3, active_ride_count=0, wkt=wkt)

    db = RouteDB(_make_config(pg))
    try:
        routes = db.get_active_routes()
    finally:
        db.close()

    match = next((r for r in routes if r.route_id == str(route_id)), None)
    assert match is not None, "seeded route not returned"
    assert match.driver_id == str(driver_id)
    assert match.seats_available == 3
    assert len(match.route_points) == 2
    assert abs(match.route_points[0].lat - 52.2) < 1e-6
    assert abs(match.route_points[0].lon - 21.0) < 1e-6
    assert abs(match.route_points[1].lat - 52.3) < 1e-6
    assert abs(match.route_points[1].lon - 21.1) < 1e-6


def test_get_active_routes_excludes_full_route(pg):
    route_id  = uuid.uuid4()
    driver_id = uuid.uuid4()
    wkt = "LINESTRING(21.0 52.2, 21.1 52.3)"

    with psycopg.connect(_conninfo(pg)) as conn:
        _insert_route(conn, route_id=route_id, driver_id=driver_id,
                      status="Active", capacity=1, active_ride_count=1, wkt=wkt)

    db = RouteDB(_make_config(pg))
    try:
        routes = db.get_active_routes()
    finally:
        db.close()

    assert all(r.route_id != str(route_id) for r in routes), "full route should be excluded"


def test_get_active_routes_excludes_non_active_status(pg):
    route_id  = uuid.uuid4()
    driver_id = uuid.uuid4()
    wkt = "LINESTRING(21.0 52.2, 21.1 52.3)"

    with psycopg.connect(_conninfo(pg)) as conn:
        _insert_route(conn, route_id=route_id, driver_id=driver_id,
                      status="Created", capacity=3, active_ride_count=0, wkt=wkt)

    db = RouteDB(_make_config(pg))
    try:
        routes = db.get_active_routes()
    finally:
        db.close()

    assert all(r.route_id != str(route_id) for r in routes), "non-Active route should be excluded"


def test_get_active_routes_excludes_null_geometry(pg):
    route_id  = uuid.uuid4()
    driver_id = uuid.uuid4()

    with psycopg.connect(_conninfo(pg)) as conn:
        _insert_route(conn, route_id=route_id, driver_id=driver_id,
                      status="Active", capacity=3, active_ride_count=0, wkt=None)

    db = RouteDB(_make_config(pg))
    try:
        routes = db.get_active_routes()
    finally:
        db.close()

    assert all(r.route_id != str(route_id) for r in routes), "route without geometry should be excluded"
