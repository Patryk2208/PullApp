from dataclasses import dataclass, field
from typing import Optional, Union
from datetime import datetime
from algorithms import (
    BestRouteParams,
    ClosestRoutesParams,
    # CoveringRouteParams,
    # OptimalSetParams,
    AlgorithmUnion
)
from results import AlgorithmResult
from common import AlgorithmType, JobStatus


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
            error=error
        )