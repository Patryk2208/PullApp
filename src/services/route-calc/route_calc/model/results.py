from dataclasses import dataclass, field
from typing import List, Optional

from route_calc.model.common import Point

from route_calc.generated.queue_pb2 import BestRouteResult as ProtoBestRouteResult
from route_calc.generated.queue_pb2 import ClosestRoute as ProtoClosestRoute
from route_calc.generated.queue_pb2 import ClosestRoutesResult as ProtoClosestRoutesResult

@dataclass
class BestRouteResult:
    points: list[Point]
    distance_meters: float
    duration_seconds: float

    def to_proto(self) -> ProtoBestRouteResult:
        proto = ProtoBestRouteResult(
            distance_meters=self.distance_meters,
            duration_seconds=self.duration_seconds
        )
        proto.points.extend([p.to_proto() for p in self.points])
        return proto

    @classmethod
    def from_proto(cls, proto: ProtoBestRouteResult) -> "BestRouteResult":
        return cls(
            points=[Point.from_proto(p) for p in proto.points],
            distance_meters=proto.distance_meters,
            duration_seconds=proto.duration_seconds
        )

@dataclass
class ClosestRoute:
    route_id: str
    distance_to_point_meters: float
    access_point: Point

@dataclass
class ClosestRoutesResult:
    routes: List[ClosestRoute]


@dataclass
class ClosestRoute:
    route_id: str
    distance_to_point_meters: float
    access_point: Point

    def to_proto(self) -> ProtoClosestRoute:
        proto = ProtoClosestRoute(
            route_id=self.route_id,
            distance_to_point_meters=self.distance_to_point_meters
        )
        proto.access_point.CopyFrom(self.access_point.to_proto())
        return proto

    @classmethod
    def from_proto(cls, proto: ProtoClosestRoute) -> "ClosestRoute":
        return cls(
            route_id=proto.route_id,
            distance_to_point_meters=proto.distance_to_point_meters,
            access_point=Point.from_proto(proto.access_point)
        )


@dataclass
class ClosestRoutesResult:
    routes: list[ClosestRoute]

    def to_proto(self) -> ProtoClosestRoutesResult:
        proto = ProtoClosestRoutesResult()
        proto.routes.extend([r.to_proto() for r in self.routes])
        return proto

    @classmethod
    def from_proto(cls, proto: ProtoClosestRoutesResult) -> "ClosestRoutesResult":
        return cls(
            routes=[ClosestRoute.from_proto(r) for r in proto.routes]
        )

AlgorithmResult = BestRouteResult | ClosestRoutesResult