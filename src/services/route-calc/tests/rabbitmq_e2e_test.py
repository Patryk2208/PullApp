"""
End-to-end tests: RabbitMQ -> route-calc consumer -> C++ ride_matching.

Requires the route-calc service to be running (see docker-compose tests profile).

Run:
  cd src/infrastructure
  docker-compose --profile e2e run --rm route-calc-e2e-tests
"""
import asyncio
import os
import uuid

import aio_pika
import pytest
from aio_pika.exceptions import QueueEmpty

from route_calc.model.algorithms import DriverRoute, RideMatchingQuery
from route_calc.model.common import AlgorithmType, JobStatus, Point
from route_calc.model.messages import ComputeMessage, ResultMessage
from route_calc.model.results import RideMatchingResult


def rabbitmq_url() -> str:
    host = os.getenv("RABBITMQ_HOST", "localhost")
    port = os.getenv("RABBITMQ_PORT", "5672")
    user = os.getenv("RABBITMQ_USER", "pullapp")
    password = os.getenv("RABBITMQ_PASS", "pullapp-secret")
    return f"amqp://{user}:{password}@{host}:{port}/"


COMPUTE_QUEUE = os.getenv("RABBITMQ_COMPUTE_QUEUE", "compute-queue")
RESULTS_QUEUE = os.getenv("RABBITMQ_RESULTS_QUEUE", "results-queue")


async def purge_queues() -> None:
    """Remove stale messages before route-calc starts consuming."""
    connection = await aio_pika.connect_robust(rabbitmq_url())
    async with connection:
        channel = await connection.channel()
        for queue_name in (COMPUTE_QUEUE, RESULTS_QUEUE):
            queue = await channel.declare_queue(queue_name, durable=True)
            await queue.purge()


def make_warsaw_krakow_ride_matching_job(job_id: str) -> ComputeMessage:
    """Passenger and driver both Warsaw -> Krakow; should match via C++."""
    start = Point(lat=52.2297, lon=21.0122)
    end = Point(lat=50.0647, lon=19.9450)
    driver_route = DriverRoute(
        route_id="route-e2e-1",
        driver_id="driver-e2e-1",
        route_points=[start, end],
        departure_date=1_700_000_000,
        departure_time_minutes=480,
        seats_available=3,
        estimated_duration_hours=4.0,
    )
    return ComputeMessage(
        job_id=job_id,
        algorithm=AlgorithmType.RIDE_MATCHING,
        params=RideMatchingQuery(
            passenger_id="passenger-e2e-1",
            start=start,
            end=end,
            departure_date=1_700_000_000,
            seats_needed=1,
            candidate_routes=[driver_route],
            max_detour_km=50,
            time_window_minutes=120,
        ),
    )


async def publish_job(channel: aio_pika.abc.AbstractChannel, job: ComputeMessage) -> None:
    await channel.default_exchange.publish(
        aio_pika.Message(
            body=job.to_proto().SerializeToString(),
            content_type="application/protobuf",
            delivery_mode=aio_pika.DeliveryMode.PERSISTENT,
        ),
        routing_key=COMPUTE_QUEUE,
    )


async def wait_for_result(job_id: str, timeout: float = 30.0) -> ResultMessage:
    connection = await aio_pika.connect_robust(rabbitmq_url())

    async with connection:
        channel = await connection.channel()
        results_queue = await channel.declare_queue(RESULTS_QUEUE, durable=True)
        deadline = asyncio.get_running_loop().time() + timeout

        while asyncio.get_running_loop().time() < deadline:
            remaining = deadline - asyncio.get_running_loop().time()
            try:
                message = await asyncio.wait_for(
                    results_queue.get(no_ack=False, fail=False, timeout=min(remaining, 2.0)),
                    timeout=min(remaining, 2.0) + 1.0,
                )
            except (asyncio.TimeoutError, QueueEmpty):
                continue

            if message is None:
                continue

            parsed = ResultMessage.from_proto(message.body)
            if parsed.job_id == job_id:
                await message.ack()
                return parsed
            await message.ack()

    raise TimeoutError(f"No result for job {job_id} within {timeout}s")


@pytest.mark.asyncio
@pytest.mark.e2e
async def test_ride_matching_through_rabbitmq_and_cpp():
    """Publish protobuf job; route-calc runs C++ match_single_route; result on results-queue."""
    await purge_queues()

    job_id = f"e2e-ride-matching-{uuid.uuid4().hex[:12]}"
    job = make_warsaw_krakow_ride_matching_job(job_id)

    connection = await aio_pika.connect_robust(rabbitmq_url())
    async with connection:
        channel = await connection.channel()
        await publish_job(channel, job)

    result = await wait_for_result(job_id, timeout=30.0)

    assert result.status == JobStatus.SUCCESS
    assert isinstance(result.result, RideMatchingResult)
    assert len(result.result.matches) >= 1

    match = result.result.matches[0]
    assert match.route_id == "route-e2e-1"
    assert match.driver_id == "driver-e2e-1"
    assert match.match_score > 0.9
    assert match.pickup_point_index == 0
    assert match.dropoff_point_index == 1
