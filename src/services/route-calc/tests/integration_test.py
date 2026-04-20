import asyncio
import json
import random

import aio_pika
import pytest
from aio_pika import connect_robust, Message

from route_calc.model.common import AlgorithmType
from route_calc.model.messages import ComputeMessage


@pytest.mark.asyncio
async def test_flow(mock_compute_message_factory):
    with open("./src/route_calc/generated/config.json", "r", encoding="utf-8") as f:
        cfg = json.load(f)
    connection_string = f"amqp://{cfg['queue']["username"]}:{cfg['queue']['password']}@{cfg['queue']['host']}:{cfg['queue']['port']}"
    connection = await connect_robust(url=connection_string)
    channel = await connection.channel()
    await channel.set_qos(prefetch_count=cfg['queue']['prefetch_count'])
    result_queue = await channel.declare_queue(name=cfg['queue']['results'], durable=True)
    result_pull_based_queue = asyncio.Queue()

    async def handler(message):
        await result_pull_based_queue.put(message.body.decode())
        await message.ack()

    await result_queue.consume(handler)

    for i in range(100):
        payload = mock_compute_message_factory()
        await asyncio.sleep(random.randint(0, 1))
        await channel.default_exchange.publish(
            Message(body=payload.to_proto().SerializeToString(), delivery_mode=aio_pika.DeliveryMode.PERSISTENT),
            routing_key=cfg['queue']['compute'],
        )

        msg = await asyncio.wait_for(result_queue.get(), timeout=2)
        result = ComputeMessage.from_proto(msg)
        assert result.job_id == payload.job_id

    await channel.close()
    await connection.close()
