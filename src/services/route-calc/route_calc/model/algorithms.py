from dataclasses import dataclass, field
from typing import List, Optional, Union
from pydantic import BaseModel, Field
from route_calc.model.common import Point, CostType

from route_calc.generated.queue_pb2 import RideMatchingQuery as ProtoRideMatchingQuery
from route_calc.generated.queue_pb2 import BestRouteParams as ProtoBestRouteParams
from route_calc.generated.queue_pb2 import ClosestRoutesParams as ProtoClosestRoutesParams


@dataclass
class BestRouteParams:
    start: Point
    end: Point
    cost_type: str

    def to_proto(self) -> ProtoBestRouteParams:
        proto = ProtoBestRouteParams(cost_type=self.cost_type)
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
        proto = ProtoClosestRoutesParams(k=self.k, radius_meters=self.radius_meters)
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
    seats_available: int
    departure_date: int = 0
    departure_time_minutes: int = 0
    estimated_duration_hours: float = 0.0


@dataclass
class RideMatchingQuery:
    passenger_id: str
    start: Point
    end: Point
    departure_date: int
    seats_needed: int
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
        return proto

    @classmethod
    def from_proto(cls, proto: ProtoRideMatchingQuery) -> "RideMatchingQuery":
        return cls(
            passenger_id=proto.passenger_id,
            start=Point.from_proto(proto.start),
            end=Point.from_proto(proto.end),
            departure_date=proto.departure_date,
            seats_needed=proto.seats_needed,
            max_detour_km=proto.max_detour_km,
            time_window_minutes=proto.time_window_minutes
        )


AlgorithmUnion = RideMatchingQuery | BestRouteParams | ClosestRoutesParams
