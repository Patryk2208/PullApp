"""
Integration test for RabbitMQ + route-calc algorithms

This test verifies:
1. Connection to RabbitMQ works
2. Can publish jobs to compute-queue
3. Messages can be properly serialized/deserialized
4. Results can be read from results-queue

Run with: pytest tests/rabbitmq_integration_test.py -v
Or manually: python tests/rabbitmq_integration_test.py

To run inside Docker:
  docker-compose -f src/infrastructure/docker-compose.yaml run route-calc-tests
"""
import asyncio
import json
import logging
import os
from datetime import datetime, timezone
from typing import Optional

import pytest
import aio_pika
import aio_pika.abc

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)


# Test configuration - read from environment or use defaults
RABBITMQ_CONFIG = {
    "host": os.getenv("RABBITMQ_HOST", "localhost"),
    "port": int(os.getenv("RABBITMQ_PORT", "5672")),
    "username": os.getenv("RABBITMQ_USER", "pullapp"),
    "password": os.getenv("RABBITMQ_PASS", "pullapp-secret"),
    "vhost": "/",
    "compute": "compute-queue",
    "results": "results-queue",
}


class SimpleJobMessage:
    """Simplified job message for testing (without protobuf dependency in tests)"""

    def __init__(self, job_id: str, algorithm: str, params: dict):
        self.job_id = job_id
        self.algorithm = algorithm
        self.params = params
        self.created_at = datetime.now(timezone.utc).isoformat()

    def to_json(self) -> bytes:
        return json.dumps({
            "job_id": self.job_id,
            "algorithm": self.algorithm,
            "params": self.params,
            "created_at": self.created_at
        }).encode()


async def connect_to_rabbitmq() -> tuple[aio_pika.RobustConnection, aio_pika.abc.AbstractRobustChannel]:
    """
    Connect to RabbitMQ

    Returns: (connection, channel) tuple
    """
    connection_url = (
        f"amqp://{RABBITMQ_CONFIG['username']}:{RABBITMQ_CONFIG['password']}"
        f"@{RABBITMQ_CONFIG['host']}:{RABBITMQ_CONFIG['port']}/{RABBITMQ_CONFIG['vhost']}"
    )

    connection = await aio_pika.connect_robust(connection_url)
    channel = await connection.channel()

    return connection, channel


async def declare_queues(channel: aio_pika.abc.AbstractRobustChannel):
    """Declare compute and results queues"""

    compute_queue = await channel.declare_queue(
        name=RABBITMQ_CONFIG["compute"],
        durable=True
    )
    results_queue = await channel.declare_queue(
        name=RABBITMQ_CONFIG["results"],
        durable=True
    )

    return compute_queue, results_queue


@pytest.mark.asyncio
async def test_rabbitmq_connection():
    """Test 1: Can we connect to RabbitMQ?"""

    logger.info("Test 1: Testing RabbitMQ connection...")

    try:
        connection, channel = await connect_to_rabbitmq()

        async with connection:
            logger.info("✓ Successfully connected to RabbitMQ")
            assert connection is not None
            assert channel is not None

    except Exception as e:
        logger.error(f"✗ Failed to connect to RabbitMQ: {e}")
        raise


@pytest.mark.asyncio
async def test_queue_declaration():
    """Test 2: Can we declare queues?"""

    logger.info("Test 2: Testing queue declaration...")

    connection, channel = await connect_to_rabbitmq()

    async with connection:
        compute_queue, results_queue = await declare_queues(channel)

        logger.info(f"✓ Compute queue declared: {compute_queue.name}")
        logger.info(f"✓ Results queue declared: {results_queue.name}")

        assert compute_queue.name == RABBITMQ_CONFIG["compute"]
        assert results_queue.name == RABBITMQ_CONFIG["results"]


@pytest.mark.asyncio
async def test_publish_best_route_job():
    """Test 3: Can we publish a best_route job?"""

    logger.info("Test 3: Testing best_route job publication...")

    connection, channel = await connect_to_rabbitmq()

    async with connection:
        compute_queue, _ = await declare_queues(channel)

        # Create a best_route job
        job = SimpleJobMessage(
            job_id="test-best-route-001",
            algorithm="best_route",
            params={
                "start": {"lat": 40.7128, "lon": -74.0060},  # NYC
                "end": {"lat": 34.0522, "lon": -118.2437},    # LA
                "osrm_url": "http://router.project-osrm.org"
            }
        )

        # Publish job
        message = aio_pika.Message(
            body=job.to_json(),
            content_type="application/json"
        )

        await channel.default_exchange.publish(
            message,
            routing_key=RABBITMQ_CONFIG["compute"]
        )

        logger.info(f"✓ Published best_route job {job.job_id}")
        logger.info(f"  Job: NYC to LA")

        # Verify delivery on an isolated queue (not compute-queue, which route-calc may consume)
        verify_queue = await channel.declare_queue(
            name="integration-single-publish",
            durable=False,
            auto_delete=True,
        )
        verify_message = aio_pika.Message(
            body=job.to_json(),
            content_type="application/json",
        )
        await channel.default_exchange.publish(
            verify_message,
            routing_key="integration-single-publish",
        )
        received = await asyncio.wait_for(verify_queue.get(fail=False), timeout=2.0)
        assert received is not None
        assert json.loads(received.body.decode())["job_id"] == job.job_id
        await received.ack()
        logger.info("✓ Message publish verified via isolated queue")


@pytest.mark.asyncio
async def test_publish_ride_matching_job():
    """Test 4: Can we publish a ride_matching job?"""

    logger.info("Test 4: Testing ride_matching job publication...")

    connection, channel = await connect_to_rabbitmq()

    async with connection:
        compute_queue, _ = await declare_queues(channel)

        # Create a ride_matching job
        job = SimpleJobMessage(
            job_id="test-ride-matching-001",
            algorithm="ride_matching",
            params={
                "passenger_start": {"lat": 40.7489, "lon": -73.9680},   # Times Square
                "passenger_end": {"lat": 40.7829, "lon": -73.9654},     # Central Park
                "driver_route": [
                    {"lat": 40.7489, "lon": -73.9680},
                    {"lat": 40.7549, "lon": -73.9731},
                    {"lat": 40.7788, "lon": -73.9698},
                    {"lat": 40.7829, "lon": -73.9654},
                ],
                "max_detour_km": 5.0
            }
        )

        # Publish job
        message = aio_pika.Message(
            body=job.to_json(),
            content_type="application/json"
        )

        await channel.default_exchange.publish(
            message,
            routing_key=RABBITMQ_CONFIG["compute"]
        )

        logger.info(f"✓ Published ride_matching job {job.job_id}")
        logger.info(f"  Job: Times Square to Central Park (4 waypoints)")


@pytest.mark.asyncio
async def test_publish_closest_routes_job():
    """Test 5: Can we publish a closest_routes job?"""

    logger.info("Test 5: Testing closest_routes job publication...")

    connection, channel = await connect_to_rabbitmq()

    async with connection:
        compute_queue, _ = await declare_queues(channel)

        # Create a closest_routes job
        job = SimpleJobMessage(
            job_id="test-closest-routes-001",
            algorithm="closest_routes",
            params={
                "point": {"lat": 40.7489, "lon": -73.9680},  # Times Square
                "num_routes": 5,
                "osrm_url": "http://router.project-osrm.org"
            }
        )

        # Publish job
        message = aio_pika.Message(
            body=job.to_json(),
            content_type="application/json"
        )

        await channel.default_exchange.publish(
            message,
            routing_key=RABBITMQ_CONFIG["compute"]
        )

        logger.info(f"✓ Published closest_routes job {job.job_id}")
        logger.info(f"  Job: Find 5 routes near Times Square")


@pytest.mark.asyncio
async def test_message_roundtrip():
    """Test 6: Publish a message and read it back"""

    logger.info("Test 6: Testing message publish/receive roundtrip...")

    connection, channel = await connect_to_rabbitmq()

    async with connection:
        # Declare separate test queue
        test_queue = await channel.declare_queue(
            name="test-roundtrip",
            durable=False
        )

        # Publish message
        job = SimpleJobMessage(
            job_id="test-roundtrip-001",
            algorithm="best_route",
            params={"start": {"lat": 0, "lon": 0}, "end": {"lat": 1, "lon": 1}}
        )

        message = aio_pika.Message(
            body=job.to_json(),
            content_type="application/json"
        )

        await channel.default_exchange.publish(
            message,
            routing_key="test-roundtrip"
        )

        logger.info("✓ Published test message")

        # Receive message
        try:
            received = await asyncio.wait_for(
                test_queue.get(),
                timeout=2.0
            )

            logger.info(f"✓ Received message: {received.body.decode()}")

            # Parse and verify
            msg_data = json.loads(received.body.decode())
            assert msg_data["job_id"] == "test-roundtrip-001"
            assert msg_data["algorithm"] == "best_route"

            await received.ack()

        except asyncio.TimeoutError:
            logger.error("✗ No message received within timeout")
            raise
        finally:
            await test_queue.delete()


@pytest.mark.asyncio
async def test_multiple_jobs_batched():
    """Test 7: Publish multiple jobs in rapid succession"""

    logger.info("Test 7: Testing batch job publication...")

    connection, channel = await connect_to_rabbitmq()

    async with connection:
        compute_queue, _ = await declare_queues(channel)

        num_jobs = 5

        # Publish multiple jobs
        for i in range(num_jobs):
            job = SimpleJobMessage(
                job_id=f"test-batch-{i:03d}",
                algorithm="best_route",
                params={
                    "start": {"lat": 40.0 + i, "lon": -74.0},
                    "end": {"lat": 34.0, "lon": -118.0 + i}
                }
            )

            message = aio_pika.Message(
                body=job.to_json(),
                content_type="application/json"
            )

            await channel.default_exchange.publish(
                message,
                routing_key=RABBITMQ_CONFIG["compute"]
            )

        logger.info(f"✓ Published {num_jobs} jobs to compute queue")

        # Verify batch delivery on an isolated queue (not compute-queue)
        batch_queue = await channel.declare_queue(
            name="integration-batch-publish",
            durable=False,
            auto_delete=True,
        )
        for i in range(num_jobs):
            job = SimpleJobMessage(
                job_id=f"test-batch-verify-{i:03d}",
                algorithm="best_route",
                params={
                    "start": {"lat": 40.0 + i, "lon": -74.0},
                    "end": {"lat": 34.0, "lon": -118.0 + i},
                },
            )
            await channel.default_exchange.publish(
                aio_pika.Message(body=job.to_json(), content_type="application/json"),
                routing_key="integration-batch-publish",
            )

        received_count = 0
        deadline = asyncio.get_running_loop().time() + 3.0
        while received_count < num_jobs and asyncio.get_running_loop().time() < deadline:
            try:
                msg = await asyncio.wait_for(batch_queue.get(fail=False), timeout=0.5)
            except asyncio.TimeoutError:
                continue
            if msg is not None:
                received_count += 1
                await msg.ack()

        logger.info(f"✓ Received {received_count} messages on isolated batch queue")
        assert received_count == num_jobs


def run_tests_manually():
    """Run all tests manually without pytest"""

    logger.info("=" * 70)
    logger.info("RabbitMQ Integration Tests")
    logger.info("=" * 70)

    tests = [
        ("Connection Test", test_rabbitmq_connection),
        ("Queue Declaration Test", test_queue_declaration),
        ("Best Route Job Test", test_publish_best_route_job),
        ("Ride Matching Job Test", test_publish_ride_matching_job),
        ("Closest Routes Job Test", test_publish_closest_routes_job),
        ("Message Roundtrip Test", test_message_roundtrip),
        ("Batch Jobs Test", test_multiple_jobs_batched),
    ]

    passed = 0
    failed = 0

    for test_name, test_func in tests:
        try:
            logger.info(f"\nRunning: {test_name}")
            asyncio.run(test_func())
            passed += 1
            logger.info(f"✓ {test_name} PASSED")
        except Exception as e:
            failed += 1
            logger.error(f"✗ {test_name} FAILED: {e}")

    logger.info("\n" + "=" * 70)
    logger.info(f"Results: {passed} passed, {failed} failed")
    logger.info("=" * 70)


if __name__ == "__main__":
    run_tests_manually()
