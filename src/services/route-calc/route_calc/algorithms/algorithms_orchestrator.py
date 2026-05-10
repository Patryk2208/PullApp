import logging
import random
from time import sleep
from typing import Callable, Dict

from route_calc.model.common import AlgorithmType, Point
from route_calc.model.messages import ComputeMessage, ResultMessage
from route_calc.model.results import (
    BestRouteResult, ClosestRoutesResult, ClosestRoute,
    RideMatchingResult, MatchedRoute
)

from route_calc.algorithms import slow_algorithm
from route_calc.algorithms import match_single_route as cpp_match_single_route
from route_calc.algorithms import Point as CppPoint

class AlgorithmsOrchestrator:
    def __init__(self, config: dict, logger: logging.Logger):
        self.config = config
        self.logger = logger
        self.algorithms: Dict[AlgorithmType, Callable] = {
            AlgorithmType.BEST_ROUTE: self._run_best_route,
            AlgorithmType.CLOSEST_ROUTES: self._run_closest_routes,
            AlgorithmType.RIDE_MATCHING: self._run_ride_matching,
        }

    def compute(self, payload: ComputeMessage) -> ResultMessage:
        self.logger.info(f"Computing {payload.algorithm} for job {payload.job_id}")
        slow_algorithm(0, 2)
        if random.random() < 0.1:
            return ResultMessage.failure(payload.job_id, "Mocked failure", 500)

        algorithm_handler = self.algorithms.get(payload.algorithm)
        if not algorithm_handler:
            raise ValueError(f"Unsupported algorithm: {payload.algorithm}")

        res = algorithm_handler(payload)
        return ResultMessage.success(payload.job_id, res, 2000)

    def _run_best_route(self, payload: ComputeMessage) -> BestRouteResult:
        """Mock implementation of BEST_ROUTE algorithm"""
        return BestRouteResult(
            points=[Point(lat=4, lon=7), Point(lat=1, lon=2)],
            distance_meters=1000,
            duration_seconds=100
        )

    def _run_closest_routes(self, payload: ComputeMessage) -> ClosestRoutesResult:
        """Mock implementation of CLOSEST_ROUTES algorithm"""
        return ClosestRoutesResult(
            routes=[
                ClosestRoute(
                    route_id="1",
                    distance_to_point_meters=100,
                    access_point=Point(lat=4, lon=7)
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
