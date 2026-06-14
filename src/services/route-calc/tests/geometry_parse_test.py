"""Offline tests for OSRM GeoJSON geometry parsing (no network required)."""

import pytest

from route_calc.algorithms import parse_geometry_from_json


SAMPLE_ROUTE_JSON = """
{
    "distance": 12345.6,
    "duration": 789.0,
    "geometry": {
        "type": "LineString",
        "coordinates": [
            [21.0122, 52.2297],
            [21.0130, 52.2300],
            [19.9450, 50.0647]
        ]
    }
}
"""


def test_parse_geometry_returns_full_linestring():
    points = parse_geometry_from_json(SAMPLE_ROUTE_JSON)

    assert len(points) == 3
    assert abs(points[0].lat - 52.2297) < 1e-6
    assert abs(points[0].lon - 21.0122) < 1e-6
    assert abs(points[-1].lat - 50.0647) < 1e-6
    assert abs(points[-1].lon - 19.9450) < 1e-6


def test_parse_geometry_returns_empty_for_encoded_polyline():
    encoded = '{"geometry":"obx}Hm}f_CAEYBS"}'
    assert parse_geometry_from_json(encoded) == []
