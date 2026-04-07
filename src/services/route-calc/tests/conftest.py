import asyncio
import random
from typing import Any, List

import pytest
from time import sleep

from route_calc.model.common import AlgorithmType, Point, JobStatus
from route_calc.model.messages import ComputeMessage, ResultMessage


class MockQueue:
    def __init__(self, config: dict, mock_messages: List[ComputeMessage]):
        self.test_config = config
        self.mock_messages = mock_messages

        self.started_correctly = False
        self.jobs = {}

    async def start(self):
        if self.test_config["start_mode"] == "fail":
            await asyncio.sleep(1)
            raise ConnectionError("Mocked start failure")
        elif self.test_config["start_mode"] == "timeout":
            await asyncio.sleep(self.test_config["start_timeout_seconds"])
            raise TimeoutError("Mocked start timeout")
        else:
            self.started_correctly = True

    async def consume_job(self) -> Any:
        while [True if o not in ['acked', 'nacked'] else False for o in self.jobs.values()].count(True) >= self.test_config["prefetch_count"]:
            await asyncio.sleep(0.05)
        await asyncio.sleep(random.randint(0, 1))
        class MyMsg:
            pass
        msg = MyMsg()
        msg.__setattr__("body", self.mock_messages[random.randint(0, len(self.mock_messages) - 1)])
        self.jobs[msg.body.job_id] = 'consumed'
        return msg

    async def publish_result(self, result: Any):
        self.jobs[result.job_id] = 'published'
        await asyncio.sleep(random.randint(0, 1))

    async def ack(self, msg):
        if self.jobs[msg.body.job_id] in ['acked', 'nacked']:
            raise Exception(f"Job {msg.body.job_id} already {self.jobs[msg.body.job_id]}")
        self.jobs[msg.body.job_id] = 'acked'
        await asyncio.sleep(random.randint(0, 1))

    async def nack(self, msg, requeue):
        if self.jobs[msg.body.job_id] in ['acked', 'nacked']:
             raise Exception(f"Job {msg.body.job_id} already {self.jobs[msg.body.job_id]}")
        self.jobs[msg.body.job_id] = 'nacked'
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
        return ComputeMessage(job_id=job_id, algorithm=alg, params=None)
    return _create