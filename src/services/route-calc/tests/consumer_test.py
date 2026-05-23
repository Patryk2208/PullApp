import asyncio
import logging
import signal
import sys

import pytest

from route_calc.api.consumer import Consumer


@pytest.mark.asyncio
@pytest.mark.parametrize("config", [
    {"thread_pool": {"max_workers": 4, "shutdown_timeout_seconds": 1}, "test": {"mock_queue": {"start_mode": "normal", "prefetch_count": 4}, "mock_algorithm": {"min_compute_time": 1, "max_compute_time": 4, "failure_probability": 0}, "run_time": 5, "stop_method": signal.SIGINT}},
    {"thread_pool": {"max_workers": 4, "shutdown_timeout_seconds": 1}, "test": {"mock_queue": {"start_mode": "normal", "prefetch_count": 4}, "mock_algorithm": {"min_compute_time": 0, "max_compute_time": 5, "failure_probability": 0.2}, "run_time": 3, "stop_method": signal.SIGTERM}},
    {"thread_pool": {"max_workers": 4, "shutdown_timeout_seconds": 0}, "test": {"mock_queue": {"start_mode": "normal", "prefetch_count": 4}, "mock_algorithm": {"min_compute_time": 0, "max_compute_time": 5, "failure_probability": 0.5}, "run_time": 2, "stop_method": signal.SIGINT}}
])
async def test_consumer_flow(mock_queue_factory, mock_algorithm_factory, mock_compute_message_factory, config: dict):
    # arrange
    possible_messages = [mock_compute_message_factory() for _ in range(100)]
    logging.basicConfig(
        level=logging.DEBUG,
        stream=sys.stdout,
        format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
        force=True,  # IMPORTANT in tests
    )
    consumer = Consumer(
        config=config,
        queue=mock_queue_factory(config["test"]["mock_queue"], possible_messages),
        alg_orchestrator=mock_algorithm_factory(config["test"]["mock_algorithm"]),
        logger=logging.getLogger(__name__)
    )

    # act
    test_task = asyncio.create_task(consumer.run())
    await asyncio.sleep(config["test"]["run_time"])
    consumer.shutdown_event.set()
    await test_task

    # assert
    assert not consumer.jobs
    assert consumer.queue.started_correctly == True
    print(consumer.queue.jobs)
    assert all(val in ['acked','nacked'] for val in consumer.queue.jobs.values()), 'Not all jobs were [n]acked'
