from dataclasses import dataclass, field
from typing import List, Optional
from pydantic import BaseModel, Field
from route_calc.model.common import Point, CostType

from route_calc.generated.queue_pb2 import BestRouteParams as ProtoBestRouteParams
from route_calc.generated.queue_pb2 import ClosestRoutesParams as ProtoClosestRoutesParams

@dataclass
class BestRouteParams:
    start: Point
    end: Point
    cost_type: str

    def to_proto(self) -> ProtoBestRouteParams:
        proto = ProtoBestRouteParams(
            cost_type=self.cost_type
        )
        proto.start.CopyFrom(self.start.to_proto())
        proto.end.CopyFrom(self.end.to_proto())
        return proto

    @classmethod
    def from_proto(cls, proto: ProtoBestRouteParams) -> "BestRouteParams":
        return cls(
            start=Point.from_proto(proto.start),
            end=Point.from_proto(proto.end),
            cost_type=proto.cost_type
        )

@dataclass
class ClosestRoutesParams:
    point: Point
    k: int
    radius_meters: float

    def to_proto(self) -> ProtoClosestRoutesParams:
        proto = ProtoClosestRoutesParams(
            k=self.k,
            radius_meters=self.radius_meters
        )
        proto.point.CopyFrom(self.point.to_proto())
        return proto

    @classmethod
    def from_proto(cls, proto: ProtoClosestRoutesParams) -> "ClosestRoutesParams":
        return cls(
            point=Point.from_proto(proto.point),
            k=proto.k,
            radius_meters=proto.radius_meters
        )

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