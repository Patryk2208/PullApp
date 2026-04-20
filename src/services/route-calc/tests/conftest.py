import asyncio
import random
from typing import List

import pytest
from time import sleep

from route_calc.model.algorithms import BestRouteParams, ClosestRoutesParams
from route_calc.model.common import AlgorithmType, Point, JobStatus
from route_calc.model import JobContext
from route_calc.model import ComputeMessage, ResultMessage


class MockQueue:
    def __init__(self, config: dict, mock_messages: List[ComputeMessage]):
        self.test_config = config
        self.mock_messages = mock_messages

        self.started_correctly = False
        self.jobs = {}
        self.pull_based_queue: asyncio.Queue[JobContext] = asyncio.Queue(self.test_config["prefetch_count"])
        self.should_stop = False
        self.task = None

    async def start(self):
        if self.test_config["start_mode"] == "fail":
            await asyncio.sleep(1)
            raise ConnectionError("Mocked start failure")
        elif self.test_config["start_mode"] == "timeout":
            await asyncio.sleep(self.test_config["start_timeout_seconds"])
            raise TimeoutError("Mocked start timeout")
        else:
            self.started_correctly = True

    async def _consume_job(self):
        while not self.should_stop:
            await asyncio.sleep(random.randint(0, 8))
            payload = self.mock_messages[random.randint(0, len(self.mock_messages) - 1)]
            ctx = JobContext(
                queue_msg=payload.job_id,
                payload=payload,
                loop=asyncio.get_running_loop(),
            )
            self.jobs[payload.job_id] = 'consumed'
            await self.pull_based_queue.put(ctx)

    async def run_consume_job(self):
        self.task = asyncio.create_task(self._consume_job())

    async def stop_consume_job(self):
        self.should_stop = True
        self.task.cancel()

    async def publish_result(self, result: ResultMessage):
        self.jobs[result.job_id] = 'published'
        await asyncio.sleep(random.randint(0, 1))

    async def ack(self, msg: str):
        if self.jobs[msg] in ['acked', 'nacked']:
            raise Exception(f"Job {msg} already {self.jobs[msg]}")
        self.jobs[msg] = 'acked'
        await asyncio.sleep(random.randint(0, 1))

    async def nack(self, msg: str, requeue):
        if self.jobs[msg] in ['acked', 'nacked']:
             raise Exception(f"Job {msg} already {self.jobs[msg]}")
        self.jobs[msg] = 'nacked'
        await asyncio.sleep(random.randint(0, 1))

    async def stop(self):
        await asyncio.sleep(random.randint(0, 1))

@pytest.fixture
def mock_queue_factory():
    def _create(config: dict, mock_messages: List[ComputeMessage]):
        return MockQueue(config, mock_messages)
    return _create


class MockAlgorithmsOrchestrator:
    def __init__(self, config: dict):
        self.config = config
        self.calls = 0

    def compute(self, payload: ComputeMessage) -> ResultMessage:
        self.calls += 1
        slp_time = random.randint(self.config["min_compute_time"], self.config["max_compute_time"])
        sleep(slp_time)
        if random.random() < self.config["failure_probability"]:
            return ResultMessage.failure(job_id=payload.job_id, error="Mocked failure", status=JobStatus.FAILED)
        return ResultMessage.success(payload.job_id, None, slp_time * 1000)


@pytest.fixture
def mock_algorithm_factory():
    def _create(config: dict):
        return MockAlgorithmsOrchestrator(config)
    return _create

@pytest.fixture
def mock_compute_message_factory():
    def _create():
        job_id = str(random.randint(0, 100000))
        alg = random.choice(list(AlgorithmType))
        params = None
        if alg == AlgorithmType.BEST_ROUTE:
            params = BestRouteParams(
                start=Point(lat=4, lon=7),
                end=Point(lat=1, lon=2),
                cost_type="distance"
            )
        elif alg == AlgorithmType.CLOSEST_ROUTES:
            params = ClosestRoutesParams(
                point=Point(lat=4, lon=7),
                k=10,
                radius_meters=1000
            )
        else:
            raise ValueError(f"Unsupported algorithm: {alg}")
        return ComputeMessage(job_id=job_id, algorithm=alg, params=params)
    return _create