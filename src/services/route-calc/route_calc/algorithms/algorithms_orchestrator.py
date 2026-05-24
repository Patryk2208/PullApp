import logging
import os
import random
from time import sleep
from typing import Callable, Dict

from opentelemetry import metrics, trace

from route_calc.model.common import AlgorithmType, Point
from route_calc.model.messages import ComputeMessage, ResultMessage
from route_calc.model.results import (
    BestRouteResult, ClosestRoutesResult, ClosestRoute,
    RideMatchingResult, MatchedRoute
)

from route_calc.algorithms import slow_algorithm
from route_calc.algorithms import match_single_route as cpp_match_single_route
from route_calc.algorithms import Point as CppPoint

_tracer = trace.get_tracer(__name__)
_meter = metrics.get_meter(__name__)
_compute_duration = _meter.create_histogram(
    "route_calc.algorithm.duration_ms",
    unit="ms",
    description="Time spent in algorithm compute",
)
_compute_failures = _meter.create_counter(
    "route_calc.algorithm.failures",
    description="Mocked/real algorithm failures",
)


class AlgorithmsOrchestrator:
    def __init__(self, config: dict, logger: logging.Logger = None):
        self.config = config
        self.algorithms: Dict[AlgorithmType, Callable] = {
            AlgorithmType.BEST_ROUTE: self._run_best_route,
            AlgorithmType.CLOSEST_ROUTES: self._run_closest_routes,
            AlgorithmType.RIDE_MATCHING: self._run_ride_matching,
        }
        self.logger = logger or logging.getLogger(__name__)

    def compute(self, payload: ComputeMessage) -> ResultMessage:
        self.logger.info(f"Computing {payload.algorithm} for job {payload.job_id}")
        slow_seconds = int(os.getenv("ROUTE_CALC_SLOW_ALGORITHM_SECONDS", "2"))
        if slow_seconds > 0:
            slow_algorithm(0, slow_seconds)
        failure_prob = float(os.getenv("ROUTE_CALC_MOCK_FAILURE_PROBABILITY", "0.1"))
        if failure_prob > 0 and random.random() < failure_prob:
            return ResultMessage.failure(payload.job_id, "Mocked failure", 500)

        algorithm_handler = self.algorithms.get(payload.algorithm)
        if not algorithm_handler:
            raise ValueError(f"Unsupported algorithm: {payload.algorithm}")

        res = algorithm_handler(payload)
        return ResultMessage.success(payload.job_id, res, 2000)

    def _run_best_route(self, payload: ComputeMessage) -> BestRouteResult:
        """Query OSRM for best route between two points"""
        params = payload.params

        try:
            # Call C++ OSRM client via bindings
            from route_calc.algorithms import get_best_route_osrm, Point as CppPoint

            cpp_start = CppPoint(lat=params.start.lat, lon=params.start.lon)
            cpp_end = CppPoint(lat=params.end.lat, lon=params.end.lon)

            route_data = get_best_route_osrm(cpp_start, cpp_end)

            return BestRouteResult(
                points=route_data.waypoints,
                distance_meters=route_data.distance_meters,
                duration_seconds=route_data.duration_seconds
            )
        except Exception as e:
            self.logger.error(f"OSRM query failed for best_route: {e}")
            # Fallback to mock
            return BestRouteResult(
                points=[params.start, params.end],
                distance_meters=1000.0,
                duration_seconds=100.0
            )

    def _run_closest_routes(self, payload: ComputeMessage) -> ClosestRoutesResult:
        """Query OSRM for closest routes to a point"""
        params = payload.params

        try:
            # Call C++ OSRM client via bindings
            from route_calc.algorithms import get_closest_routes_osrm, Point as CppPoint

            cpp_point = CppPoint(lat=params.point.lat, lon=params.point.lon)
            num_routes = params.k if hasattr(params, 'k') else 3

            routes_data = get_closest_routes_osrm(cpp_point, num_routes)

            closest_routes = [
                ClosestRoute(
                    route_id=route.route_id,
                    distance_to_point_meters=route.distance_to_point_meters,
                    access_point=Point(lat=route.access_point.lat, lon=route.access_point.lon)
                )
                for route in routes_data
            ]

            return ClosestRoutesResult(routes=closest_routes)
        except Exception as e:
            self.logger.error(f"OSRM query failed for closest_routes: {e}")
            # Fallback to mock
            return ClosestRoutesResult(
                routes=[
                    ClosestRoute(
                        route_id="1",
                        distance_to_point_meters=100,
                        access_point=params.point
                    )
                ]
            )

    def _run_ride_matching(self, payload: ComputeMessage) -> RideMatchingResult:
        """Find driver routes that match a passenger's request"""
        return self._match_rides(payload)

    def _match_rides(self, payload: ComputeMessage) -> RideMatchingResult:
        """Find driver routes that match a passenger's request"""
        params = payload.params

        # Extract passenger query
        passenger_start = CppPoint(lat=params.start.lat, lon=params.start.lon)
        passenger_end = CppPoint(lat=params.end.lat, lon=params.end.lon)
        max_detour = params.max_detour_km if hasattr(params, 'max_detour_km') else 10.0

        matches = []

        # Match against all candidate driver routes
        for driver_route in params.candidate_routes:
            # Convert proto route points to C++ Point structs
            route_points = [CppPoint(lat=p.lat, lon=p.lon) for p in driver_route.route_points]

            # Call C++ matching algorithm
            match = cpp_match_single_route(
                passenger_start,
                passenger_end,
                driver_route.route_id,
                driver_route.driver_id,
                route_points,
                max_detour
            )

            # Only include matches with positive scores
            if match.match_score > 0.0:
                matched_route = MatchedRoute(
                    route_id=match.route_id,
                    driver_id=match.driver_id,
                    match_score=match.match_score,
                    detour_km=match.detour_km,
                    pickup_point_index=match.pickup_index,
                    dropoff_point_index=match.dropoff_index
                )
                matches.append(matched_route)

        # Sort by match score (best first)
        matches.sort(key=lambda m: m.match_score, reverse=True)

        return RideMatchingResult(matches=matches)
