from dataclasses import dataclass, field
from typing import List, Optional
from pydantic import BaseModel, Field
from common import Point, CostType

class BestRouteParams(BaseModel):
    start: Point
    end: Point
    cost_type: CostType = CostType.DISTANCE

class ClosestRoutesParams(BaseModel):
    point: Point
    k: int = Field(..., ge=1, le=100)
    radius_meters: float = Field(default=0.0, ge=0)

# class CoveringRouteParams(BaseModel):
#     driver_route: List[Point] = Field(..., min_length=2)
#     rider_start: Point
#     rider_end: Point
#     detour_penalty: float = Field(default=1.5, ge=1.0, le=10.0)
#     max_detour_meters: float = Field(default=1000.0, ge=0)
#
# class RiderRequest(BaseModel):
#     rider_id: str
#     start: Point
#     end: Point
#
# class Driver(BaseModel):
#     driver_id: str
#     current_route: List[Point] = Field(..., min_length=2)
#
# class OptimalSetParams(BaseModel):
#     riders: List[RiderRequest] = Field(..., min_length=1)
#     drivers: List[Driver] = Field(..., min_length=1)
#     pareto_size: int = Field(default=5, ge=1, le=20)
#     max_iterations: int = Field(default=1000, ge=1)

AlgorithmUnion = BestRouteParams | ClosestRoutesParams