from dataclasses import dataclass, field
from typing import Optional, Union
from datetime import datetime
from route_calc.model.algorithms import AlgorithmUnion
from route_calc.model.results import AlgorithmResult
from route_calc.model.common import AlgorithmType, JobStatus
from route_calc.generated.queue_pb2 import ComputeMessage


@dataclass
class ComputeMessage:
    """Message with compute payload from trip-planner to route-calc"""
    job_id: str
    algorithm: AlgorithmType
    params: AlgorithmUnion
    created_at: datetime = field(default_factory=datetime.utcnow)
    retry_count: int = 0
    deadline: Optional[datetime] = None

    def is_expired(self) -> bool:
        if self.deadline:
            return datetime.now() > self.deadline
        return False

    @classmethod
    def from_proto(cls, dto: ComputeMessage) -> 'ComputeMessage':
        return cls(
            job_id=dto.job_id,
            algorithm=AlgorithmType(dto.algorithm),
            params=dto.params
        )


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
    def success(cls, job_id: str, result: AlgorithmResult, computation_time_ms: float) -> 'ResultMessage':
        return cls(
            job_id=job_id,
            status=JobStatus.SUCCESS,
            result=result,
            computation_time_ms=computation_time_ms,
            algorithm_used=type(result).__name__
        )

    @classmethod
    def failure(cls, job_id: str, error: str, status: JobStatus = JobStatus.FAILED) -> 'ResultMessage':
        return cls(
            job_id=job_id,
            status=status,
            result=None,
            error=error
        )