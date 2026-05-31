"""
Tests for ride matching algorithm

Tests verify that driver routes are correctly matched to passenger queries
"""

import pytest
from route_calc.algorithms import distance_km, match_single_route, Point


class TestDistanceCalculation:
    """Test the distance calculation using Haversine formula"""
    
    def test_same_point_distance_is_zero(self):
        """Distance from a point to itself should be zero"""
        p = Point(lat=52.2297, lon=21.0122)  # Warsaw
        result = distance_km(p, p)
        assert abs(result) < 0.001
    
    def test_warsaw_to_krakow(self):
        """Test known distance: Warsaw to Krakow is ~250 km"""
        warsaw = Point(lat=52.2297, lon=21.0122)
        krakow = Point(lat=50.0647, lon=19.9450)
        result = distance_km(warsaw, krakow)
        # Should be approximately 250 km
        assert 240 < result < 260, f"Warsaw-Krakow distance {result} km not in expected range"
    
    def test_distance_is_symmetric(self):
        """Distance A?B should equal distance B?A"""
        warsaw = Point(lat=52.2297, lon=21.0122)
        krakow = Point(lat=50.0647, lon=19.9450)
        d1 = distance_km(warsaw, krakow)
        d2 = distance_km(krakow, warsaw)
        assert abs(d1 - d2) < 0.001


class TestRideMatching:
    """Test the ride matching algorithm"""
    
    def test_perfect_match_no_detour(self):
        """Perfect match: driver goes from Warsaw to Krakow, passenger too"""
        # Passenger wants to go Warsaw ? Krakow
        passenger_start = Point(lat=52.2297, lon=21.0122)
        passenger_end = Point(lat=50.0647, lon=19.9450)
        
        # Driver route: Warsaw ? Krakow (simplified: just start and end)
        driver_route = [passenger_start, passenger_end]
        
        match = match_single_route(
            passenger_start,
            passenger_end,
            "route_1",
            "driver_1",
            driver_route,
            max_detour_km=10.0
        )
        
        # Should be perfect match
        assert match.match_score > 0.9
        assert match.pickup_index == 0
        assert match.dropoff_index == 1
        assert match.detour_km < 1.0  # Minimal detour
    
    def test_passenger_start_in_middle_of_route(self):
        """Driver goes Warsaw ? Radom ? Krakow, passenger starts at Radom"""
        warsaw = Point(lat=52.2297, lon=21.0122)
        radom = Point(lat=51.4117, lon=21.7678)
        krakow = Point(lat=50.0647, lon=19.9450)
        
        # Passenger: Radom ? Krakow
        passenger_start = radom
        passenger_end = krakow
        
        # Driver route: Warsaw ? Radom ? Krakow
        driver_route = [warsaw, radom, krakow]
        
        match = match_single_route(
            passenger_start,
            passenger_end,
            "route_2",
            "driver_1",
            driver_route,
            max_detour_km=20.0
        )
        
        # Should match at indices 1?2
        assert match.match_score > 0.0
        assert match.pickup_index == 1
        assert match.dropoff_index == 2
    
    def test_no_match_passenger_end_before_start(self):
        """No match: passenger end is before start on the driver route"""
        warsaw = Point(lat=52.2297, lon=21.0122)
        radom = Point(lat=51.4117, lon=21.7678)
        krakow = Point(lat=50.0647, lon=19.9450)
        
        # Driver route: Warsaw ? Radom ? Krakow
        driver_route = [warsaw, radom, krakow]
        
        # Passenger: Krakow ? Radom (backward on driver route)
        passenger_start = krakow
        passenger_end = radom
        
        match = match_single_route(
            passenger_start,
            passenger_end,
            "route_3",
            "driver_1",
            driver_route,
            max_detour_km=10.0
        )
        
        # Should not match
        assert match.match_score == 0.0
        assert match.pickup_index == -1
    
    def test_excessive_detour_no_match(self):
        """No match: passenger would require driver to deviate too much"""
        warsaw = Point(lat=52.2297, lon=21.0122)
        krakow = Point(lat=50.0647, lon=19.9450)
        
        # Driver: Warsaw ? Krakow  (simple route)
        driver_route = [warsaw, krakow]
        
        # Passenger: wants to start 500 km away from driver route
        passenger_start = Point(lat=54.0, lon=18.0)  # Gdansk area
        passenger_end = krakow
        
        match = match_single_route(
            passenger_start,
            passenger_end,
            "route_4",
            "driver_1",
            driver_route,
            max_detour_km=10.0
        )
        
        # Should not match (too far from route)
        assert match.match_score == 0.0
    
    def test_score_decreases_with_detour(self):
        """Match score should be lower for routes with more detour"""
        warsaw = Point(lat=52.2297, lon=21.0122)
        krakow = Point(lat=50.0647, lon=19.9450)
        
        # Create a route that goes Warsaw ? Krakow
        driver_route = [warsaw, krakow]
        
        # Passenger queries 1: direct Warsaw ? Krakow
        passenger1_start = warsaw
        passenger1_end = krakow
        
        match1 = match_single_route(
            passenger1_start,
            passenger1_end,
            "route_5",
            "driver_1",
            driver_route,
            max_detour_km=50.0
        )
        
        # Passenger queries 2: slightly offset but similar route
        passenger2_start = Point(lat=52.23, lon=21.02)  # Slightly offset
        passenger2_end = krakow
        
        match2 = match_single_route(
            passenger2_start,
            passenger2_end,
            "route_5",
            "driver_1",
            driver_route,
            max_detour_km=50.0
        )
        
        # Perfect match should score higher
        assert match1.match_score > match2.match_score
        assert match1.detour_km < match2.detour_km
    
    def test_route_with_multiple_waypoints(self):
        """Test matching with a detailed route that has more waypoints"""
        # A route that zig-zags a bit
        warsaw = Point(lat=52.2297, lon=21.0122)
        midpoint1 = Point(lat=51.5, lon=20.5)
        midpoint2 = Point(lat=50.5, lon=20.0)
        krakow = Point(lat=50.0647, lon=19.9450)
        
        driver_route = [warsaw, midpoint1, midpoint2, krakow]
        
        # Passenger: Warsaw ? Krakow
        passenger_start = warsaw
        passenger_end = krakow
        
        match = match_single_route(
            passenger_start,
            passenger_end,
            "route_6",
            "driver_1",
            driver_route,
            max_detour_km=50.0
        )
        
        # Should still match well despite zig-zag
        assert match.match_score > 0.0
        assert match.pickup_index == 0
        assert match.dropoff_index == 3


class TestEdgeCases:
    """Test edge cases and error conditions"""
    
    def test_route_too_short(self):
        """Route with less than 2 points should not match"""
        warsaw = Point(lat=52.2297, lon=21.0122)
        krakow = Point(lat=50.0647, lon=19.9450)
        
        # Route with only 1 point
        driver_route = [warsaw]
        
        match = match_single_route(
            warsaw,
            krakow,
            "route_7",
            "driver_1",
            driver_route,
            max_detour_km=10.0
        )
        
        # Should not match
        assert match.match_score == 0.0
    
    def test_empty_route(self):
        """Empty route should not match"""
        warsaw = Point(lat=52.2297, lon=21.0122)
        krakow = Point(lat=50.0647, lon=19.9450)
        
        driver_route = []
        
        match = match_single_route(
            warsaw,
            krakow,
            "route_8",
            "driver_1",
            driver_route,
            max_detour_km=10.0
        )
        
        # Should not match
        assert match.match_score == 0.0
    
    def test_match_result_contains_all_fields(self):
        """Match result should contain all required fields"""
        warsaw = Point(lat=52.2297, lon=21.0122)
        krakow = Point(lat=50.0647, lon=19.9450)
        
        driver_route = [warsaw, krakow]
        
        match = match_single_route(
            warsaw,
            krakow,
            "route_9",
            "driver_1",
            driver_route,
            max_detour_km=10.0
        )
        
        # All fields should be set
        assert match.route_id == "route_9"
        assert match.driver_id == "driver_1"
        assert hasattr(match, "match_score")
        assert hasattr(match, "detour_km")
        assert hasattr(match, "pickup_index")
        assert hasattr(match, "dropoff_index")
