import logging
import random
from time import sleep

from route_calc.model.common import AlgorithmType, Point
from route_calc.model.messages import ComputeMessage, ResultMessage
from route_calc.model.results import BestRouteResult, ClosestRoutesResult, ClosestRoute

from route_calc.algorithms import slow_algorithm

class AlgorithmsOrchestrator:
    def __init__(self, config: dict, logger: logging.Logger):
        self.config = config
        self.logger = logger

    def compute(self, payload: ComputeMessage) -> ResultMessage:
        self.logger.info(f"Computing {payload.algorithm} for job {payload.job_id}")
        slow_algorithm(0, 2)
        if random.random() < 0.1:
            return ResultMessage.failure(payload.job_id, "Mocked failure", 500)
        else:
            if payload.algorithm == AlgorithmType.BEST_ROUTE:
                res = BestRouteResult(
                    points=[Point(lat=4, lon=7), Point(lat=1, lon=2)],
                    distance_meters=1000,
                    duration_seconds=100
                )
            elif payload.algorithm == AlgorithmType.CLOSEST_ROUTES:
                res = ClosestRoutesResult(
                    routes=[
                        ClosestRoute(
                            route_id="1",
                            distance_to_point_meters=100,
                            access_point=Point(lat=4, lon=7)
                        )
                    ]
                )
            else:
                raise ValueError(f"Unsupported algorithm: {payload.algorithm}")
            return ResultMessage.success(payload.job_id, res, 2000)