import logging
import random
import time

from opentelemetry import metrics, trace

from route_calc.model.common import AlgorithmType, Point
from route_calc.model.messages import ComputeMessage, ResultMessage
from route_calc.model.results import BestRouteResult, ClosestRoutesResult, ClosestRoute

from route_calc.algorithms import slow_algorithm

_tracer = trace.get_tracer(__name__)
_meter = metrics.get_meter(__name__)
_compute_duration = _meter.create_histogram(
    "route_calc.algorithm.duration_ms",
    unit="ms",
    description="Time spent in algorithm compute",
)
_compute_failures = _meter.create_counter(
    "route_calc.algorithm.failures",
    description="Mocked/real algorithm failures",
)


class AlgorithmsOrchestrator:
    def __init__(self, config: dict, logger: logging.Logger = None):
        self.config = config
        self.logger = logger or logging.getLogger(__name__)

    def compute(self, payload: ComputeMessage) -> ResultMessage:
        self.logger.info("Computing algorithm=%s job_id=%s", payload.algorithm, payload.job_id)

        start = time.monotonic()
        with _tracer.start_as_current_span(
            "algorithm.compute",
            attributes={"algorithm": str(payload.algorithm), "job_id": payload.job_id},
        ):
            self.logger.debug("Calling slow_algorithm for job_id=%s", payload.job_id)
            slow_algorithm(0, 2)

            elapsed_ms = (time.monotonic() - start) * 1000
            _compute_duration.record(elapsed_ms, {"algorithm": str(payload.algorithm)})
            self.logger.debug("Algorithm done in %.1fms job_id=%s", elapsed_ms, payload.job_id)

            if random.random() < 0.1:
                _compute_failures.add(1, {"algorithm": str(payload.algorithm)})
                self.logger.warning("Mocked failure for job_id=%s", payload.job_id)
                return ResultMessage.failure(payload.job_id, "Mocked failure", 500)

            if payload.algorithm == AlgorithmType.BEST_ROUTE:
                self.logger.debug("Building BEST_ROUTE result for job_id=%s", payload.job_id)
                res = BestRouteResult(
                    points=[Point(lat=4, lon=7), Point(lat=1, lon=2)],
                    distance_meters=1000,
                    duration_seconds=100,
                )
            elif payload.algorithm == AlgorithmType.CLOSEST_ROUTES:
                self.logger.debug("Building CLOSEST_ROUTES result for job_id=%s", payload.job_id)
                res = ClosestRoutesResult(
                    routes=[
                        ClosestRoute(
                            route_id="1",
                            distance_to_point_meters=100,
                            access_point=Point(lat=4, lon=7),
                        )
                    ]
                )
            else:
                self.logger.error("Unsupported algorithm=%s job_id=%s", payload.algorithm, payload.job_id)
                raise ValueError(f"Unsupported algorithm: {payload.algorithm}")

            self.logger.info("Algorithm succeeded job_id=%s algorithm=%s", payload.job_id, payload.algorithm)
            return ResultMessage.success(payload.job_id, res, 2000)
