from dataclasses import dataclass, field
from typing import List, Optional, Union
from pydantic import BaseModel, Field
from route_calc.model.common import Point, CostType

from route_calc.generated.queue_pb2 import RideMatchingQuery as ProtoRideMatchingQuery
from route_calc.generated.queue_pb2 import DriverRoute as ProtoDriverRoute
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

@dataclass
class DriverRoute:
    route_id: str
    driver_id: str
    route_points: List[Point]
    departure_date: int
    departure_time_minutes: int
    seats_available: int
    estimated_duration_hours: float

    def to_proto(self) -> ProtoDriverRoute:
        proto = ProtoDriverRoute(
            route_id=self.route_id,
            driver_id=self.driver_id,
            departure_date=self.departure_date,
            departure_time_minutes=self.departure_time_minutes,
            seats_available=self.seats_available,
            estimated_duration_hours=self.estimated_duration_hours
        )
        proto.route_points.extend([p.to_proto() for p in self.route_points])
        return proto

    @classmethod
    def from_proto(cls, proto: ProtoDriverRoute) -> "DriverRoute":
        return cls(
            route_id=proto.route_id,
            driver_id=proto.driver_id,
            route_points=[Point.from_proto(p) for p in proto.route_points],
            departure_date=proto.departure_date,
            departure_time_minutes=proto.departure_time_minutes,
            seats_available=proto.seats_available,
            estimated_duration_hours=proto.estimated_duration_hours
        )

@dataclass
class RideMatchingQuery:
    passenger_id: str
    start: Point
    end: Point
    departure_date: int
    seats_needed: int
    candidate_routes: List[DriverRoute]
    max_detour_km: int = 10
    time_window_minutes: int = 120

    def to_proto(self) -> ProtoRideMatchingQuery:
        proto = ProtoRideMatchingQuery(
            passenger_id=self.passenger_id,
            departure_date=self.departure_date,
            seats_needed=self.seats_needed,
            max_detour_km=self.max_detour_km,
            time_window_minutes=self.time_window_minutes
        )
        proto.start.CopyFrom(self.start.to_proto())
        proto.end.CopyFrom(self.end.to_proto())
        proto.candidate_routes.extend([r.to_proto() for r in self.candidate_routes])
        return proto

    @classmethod
    def from_proto(cls, proto: ProtoRideMatchingQuery) -> "RideMatchingQuery":
        return cls(
            passenger_id=proto.passenger_id,
            start=Point.from_proto(proto.start),
            end=Point.from_proto(proto.end),
            departure_date=proto.departure_date,
            seats_needed=proto.seats_needed,
            candidate_routes=[DriverRoute.from_proto(r) for r in proto.candidate_routes],
            max_detour_km=proto.max_detour_km,
            time_window_minutes=proto.time_window_minutes
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

AlgorithmUnion = RideMatchingQuery | BestRouteParams | ClosestRoutesParams
