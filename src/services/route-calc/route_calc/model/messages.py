from dataclasses import dataclass, field
from typing import Optional, Union
from datetime import datetime, timezone

from route_calc.model.algorithms import AlgorithmUnion, BestRouteParams, ClosestRoutesParams, RideMatchingQuery
from route_calc.model.results import AlgorithmResult, BestRouteResult, ClosestRoutesResult, RideMatchingResult
from route_calc.model.common import AlgorithmType, JobStatus
from route_calc.generated.queue_pb2 import (
    ComputeMessage as ProtoComputeMessage,
    ResultMessage as ProtoResultMessage,
)


@dataclass
class ComputeMessage:
    """Message with compute payload from trip-planner to route-calc"""
    job_id: str
    algorithm: AlgorithmType
    params: AlgorithmUnion
    created_at: datetime = field(default_factory=lambda: datetime.now(timezone.utc))
    retry_count: int = 0
    deadline: Optional[datetime] = None

    def is_expired(self) -> bool:
        return self.deadline is not None and datetime.now(timezone.utc) > self.deadline

    @classmethod
    def from_proto(cls, dto) -> "ComputeMessage":
        proto = ProtoComputeMessage.FromString(dto)

        # oneof params
        params_field = proto.WhichOneof("params")

        if params_field == "best_route":
            params = BestRouteParams.from_proto(proto.best_route)
            algorithm = AlgorithmType.BEST_ROUTE

        elif params_field == "closest_routes":
            params = ClosestRoutesParams.from_proto(proto.closest_routes)
            algorithm = AlgorithmType.CLOSEST_ROUTES

        elif params_field == "ride_matching":
            params = RideMatchingQuery.from_proto(proto.ride_matching)
            algorithm = AlgorithmType.RIDE_MATCHING

        else:
            raise ValueError("Unknown params type in ComputeMessage")

        return cls(
            job_id=proto.job_id,
            algorithm=algorithm,
            params=params,
            created_at=datetime.utcfromtimestamp(proto.created_at),
            retry_count=proto.retry_count,
        )

    def to_proto(self) -> ProtoComputeMessage:
        proto = ProtoComputeMessage(
            job_id=self.job_id,
            algorithm=self.algorithm.value,
            created_at=int(self.created_at.timestamp()),
            retry_count=self.retry_count,
        )

        # oneof params
        if isinstance(self.params, BestRouteParams):
            proto.best_route.CopyFrom(self.params.to_proto())

        elif isinstance(self.params, ClosestRoutesParams):
            proto.closest_routes.CopyFrom(self.params.to_proto())

        elif isinstance(self.params, RideMatchingQuery):
            proto.ride_matching.CopyFrom(self.params.to_proto())

        else:
            raise ValueError(f"Unsupported params type: {type(self.params)}")

        return proto


@dataclass
class ResultMessage:
    """Message with results from route-calc to trip-planner"""
    job_id: str
    status: JobStatus
    result: Optional[AlgorithmResult]
    error: Optional[str] = None
    computation_time_ms: float = 0.0
    algorithm_used: Optional[str] = None

    @classmethod
    def success(
        cls,
        job_id: str,
        result: AlgorithmResult,
        computation_time_ms: float,
    ) -> "ResultMessage":
        return cls(
            job_id=job_id,
            status=JobStatus.SUCCESS,
            result=result,
            computation_time_ms=computation_time_ms,
            algorithm_used=type(result).__name__,
        )

    @classmethod
    def failure(
        cls,
        job_id: str,
        error: str,
        status: JobStatus = JobStatus.FAILED,
    ) -> "ResultMessage":
        return cls(
            job_id=job_id,
            status=status,
            result=None,
            error=error,
        )

    def to_proto(self) -> ProtoResultMessage:
        proto = ProtoResultMessage(
            job_id=self.job_id,
            success=self.status == JobStatus.SUCCESS,
            error=self.error or "",
        )

        # oneof result
        if isinstance(self.result, RideMatchingResult):
            proto.ride_matching.CopyFrom(self.result.to_proto())

        return proto

    @classmethod
    def from_proto(cls, dto) -> "ResultMessage":
        proto = ProtoResultMessage.FromString(dto)

        result_field = proto.WhichOneof("result")

        if result_field == "ride_matching":
            result = RideMatchingResult.from_proto(proto.ride_matching)
        else:
            result = None

        return cls(
            job_id=proto.job_id,
            status=JobStatus.SUCCESS if proto.success else JobStatus.FAILED,
            result=result,
            error=proto.error or None,
        )

        if isinstance(self.result, BestRouteResult):
            proto.best_route.CopyFrom(self.result.to_proto())

        elif isinstance(self.result, ClosestRoutesResult):
            proto.closest_routes.CopyFrom(self.result.to_proto())

        return proto

    @classmethod
    def from_proto(cls, dto) -> "ResultMessage":
        proto = ProtoResultMessage.FromString(dto)

        result_field = proto.WhichOneof("result")

        if result_field == "best_route":
            result = BestRouteResult.from_proto(proto.best_route)

        elif result_field == "closest_routes":
            result = ClosestRoutesResult.from_proto(proto.closest_routes)

        else:
            result = None

        return cls(
            job_id=proto.job_id,
            status=JobStatus.SUCCESS if proto.success else JobStatus.FAILED,
            result=result,
            error=proto.error or None,
        )