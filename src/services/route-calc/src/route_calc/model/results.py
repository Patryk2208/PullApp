from dataclasses import dataclass, field
from typing import List, Optional

from route_calc.model.common import Point


@dataclass
class BestRouteResult:
    points: List[Point]
    distance_meters: float
    duration_seconds: float
    algorithm_used: str

@dataclass
class ClosestRoute:
    route_id: str
    distance_to_point_meters: float
    access_point: Point

@dataclass
class ClosestRoutesResult:
    routes: List[ClosestRoute]

# @dataclass
# class CoveringRouteResult:
#     points: List[Point]
#     total_distance_meters: float
#     detour_distance_meters: float
#     original_route_distance: float
#
# @dataclass
# class OptimalSetResult:
#     solutions: List['RouteSet']
#
# @dataclass
# class RouteSet:
#     route_ids: List[str]
#     coverage_score: float
#     total_deviation_meters: float
#     drivers_used: int

AlgorithmResult = BestRouteResult | ClosestRoutesResult