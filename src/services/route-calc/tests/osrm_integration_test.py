"""Integration tests for OSRM-based route algorithms."""
import pytest
from route_calc.algorithms import (
    get_best_route_osrm, get_alternative_routes_osrm, get_closest_routes_osrm,
    Point as CppPoint, BestRouteData, ClosestRouteData
)
from route_calc.model.common import Point


class TestOSRMBestRoute:
    """Tests for best route functionality."""

    def test_best_route_returns_valid_structure(self):
        """Test that get_best_route_osrm returns BestRouteData with valid structure."""
        warsaw = CppPoint(lat=52.2297, lon=21.0122)
        krakow = CppPoint(lat=50.0647, lon=19.9450)

        result = get_best_route_osrm(warsaw, krakow)

        # Check structure
        assert hasattr(result, 'waypoints')
        assert hasattr(result, 'distance_meters')
        assert hasattr(result, 'duration_seconds')

        # Check data types
        assert isinstance(result.waypoints, list)
        assert isinstance(result.distance_meters, (int, float))
        assert isinstance(result.duration_seconds, (int, float))

        # Should have a full LineString, not just start/end fallback points
        assert len(result.waypoints) > 2

    def test_best_route_distance_reasonable(self):
        """Test that computed distance is reasonable for Warsaw-Krakow."""
        warsaw = CppPoint(lat=52.2297, lon=21.0122)
        krakow = CppPoint(lat=50.0647, lon=19.9450)

        result = get_best_route_osrm(warsaw, krakow)

        # Warsaw to Krakow is ~300km by road
        if len(result.waypoints) > 1:
            assert 250000 < result.distance_meters < 350000, \
                f"Distance {result.distance_meters}m seems unrealistic"

    def test_best_route_duration_reasonable(self):
        """Test that duration is reasonable."""
        warsaw = CppPoint(lat=52.2297, lon=21.0122)
        krakow = CppPoint(lat=50.0647, lon=19.9450)

        result = get_best_route_osrm(warsaw, krakow)

        # Should take roughly 3-5 hours (adjusted for mock estimation)
        if len(result.waypoints) > 1:
            assert 10800 < result.duration_seconds < 18000, \
                f"Duration {result.duration_seconds}s seems unrealistic"

    def test_best_route_first_waypoint_near_start(self):
        """Test that first waypoint is close to start point."""
        from route_calc.algorithms import distance_km

        warsaw = CppPoint(lat=52.2297, lon=21.0122)
        krakow = CppPoint(lat=50.0647, lon=19.9450)

        result = get_best_route_osrm(warsaw, krakow)

        if len(result.waypoints) > 0:
            dist = distance_km(result.waypoints[0], warsaw)
            assert dist < 1.0, f"First waypoint is {dist}km from start"

    def test_best_route_last_waypoint_near_end(self):
        """Test that last waypoint is close to end point."""
        from route_calc.algorithms import distance_km

        warsaw = CppPoint(lat=52.2297, lon=21.0122)
        krakow = CppPoint(lat=50.0647, lon=19.9450)

        result = get_best_route_osrm(warsaw, krakow)

        if len(result.waypoints) > 0:
            dist = distance_km(result.waypoints[-1], krakow)
            assert dist < 1.0, f"Last waypoint is {dist}km from end"


class TestOSRMAlternativeRoutes:
    """Tests for alternative routes functionality."""

    def test_alternative_routes_returns_list(self):
        """Test that get_alternative_routes_osrm returns a list."""
        warsaw = CppPoint(lat=52.2297, lon=21.0122)
        krakow = CppPoint(lat=50.0647, lon=19.9450)

        result = get_alternative_routes_osrm(warsaw, krakow, num_alternatives=3)

        assert isinstance(result, list)
        assert len(result) > 0

    def test_alternative_routes_have_waypoints(self):
        """Test that each route has valid waypoints."""
        warsaw = CppPoint(lat=52.2297, lon=21.0122)
        krakow = CppPoint(lat=50.0647, lon=19.9450)

        result = get_alternative_routes_osrm(warsaw, krakow, num_alternatives=2)

        for route in result:
            assert hasattr(route, 'waypoints')
            assert len(route.waypoints) > 2
            assert all(hasattr(p, 'lat') and hasattr(p, 'lon') for p in route.waypoints)

    def test_alternative_routes_have_metrics(self):
        """Test that routes have distance and duration metrics."""
        warsaw = CppPoint(lat=52.2297, lon=21.0122)
        krakow = CppPoint(lat=50.0647, lon=19.9450)

        result = get_alternative_routes_osrm(warsaw, krakow, num_alternatives=2)

        for route in result:
            assert hasattr(route, 'distance_meters')
            assert hasattr(route, 'duration_seconds')
            assert isinstance(route.distance_meters, (int, float))
            assert isinstance(route.duration_seconds, (int, float))


class TestOSRMClosestRoutes:
    """Tests for closest routes functionality."""

    def test_closest_routes_returns_list(self):
        """Test that get_closest_routes_osrm returns a list."""
        warsaw = CppPoint(lat=52.2297, lon=21.0122)

        result = get_closest_routes_osrm(warsaw, num_routes=3)

        assert isinstance(result, list)
        assert len(result) > 0

    def test_closest_routes_have_route_ids(self):
        """Test that routes have route IDs."""
        warsaw = CppPoint(lat=52.2297, lon=21.0122)

        result = get_closest_routes_osrm(warsaw, num_routes=3)

        for route in result:
            assert hasattr(route, 'route_id')
            assert isinstance(route.route_id, str)
            assert len(route.route_id) > 0

    def test_closest_routes_have_waypoints(self):
        """Test that each route has waypoints."""
        warsaw = CppPoint(lat=52.2297, lon=21.0122)

        result = get_closest_routes_osrm(warsaw, num_routes=3)

        for route in result:
            assert hasattr(route, 'waypoints')
            assert len(route.waypoints) > 2

    def test_closest_routes_have_distance_info(self):
        """Test that routes have distance to point information."""
        warsaw = CppPoint(lat=52.2297, lon=21.0122)

        result = get_closest_routes_osrm(warsaw, num_routes=3)

        for route in result:
            assert hasattr(route, 'distance_to_point_meters')
            assert hasattr(route, 'access_point')
            assert isinstance(route.distance_to_point_meters, (int, float))
            assert route.distance_to_point_meters >= 0

    def test_closest_routes_have_total_metrics(self):
        """Test that routes have total distance and duration metrics."""
        warsaw = CppPoint(lat=52.2297, lon=21.0122)

        result = get_closest_routes_osrm(warsaw, num_routes=3)

        for route in result:
            assert hasattr(route, 'total_distance_meters')
            assert hasattr(route, 'total_duration_seconds')
            assert isinstance(route.total_distance_meters, (int, float))
            assert isinstance(route.total_duration_seconds, (int, float))


class TestOSRMOrchestrator:
    """Integration tests with the orchestrator."""

    def test_best_route_in_orchestrator(self):
        """Test best route algorithm through orchestrator."""
        from route_calc.algorithms.algorithms_orchestrator import AlgorithmsOrchestrator
        from route_calc.model.messages import ComputeMessage
        from route_calc.model.algorithms import BestRouteParams
        from route_calc.model.common import AlgorithmType, JobStatus
        import logging

        orchestrator = AlgorithmsOrchestrator(config={}, logger=logging.getLogger(__name__))

        params = BestRouteParams(
            start=Point(lat=52.2297, lon=21.0122),
            end=Point(lat=50.0647, lon=19.9450),
            cost_type="distance"
        )

        message = ComputeMessage(
            job_id="test_best_route",
            algorithm=AlgorithmType.BEST_ROUTE,
            params=params
        )

        # Retry a few times since orchestrator randomly fails 10% of the time
        for attempt in range(5):
            result = orchestrator.compute(message)
            if result.status == JobStatus.SUCCESS and result.result is not None:
                break

        # Should eventually succeed
        assert result.status == JobStatus.SUCCESS, f"Expected success, got status {result.status}"
        assert result.result is not None, "Expected non-None result"
        assert len(result.result.points) > 2, "Expected full route LineString, not start/end fallback"
