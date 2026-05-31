import asyncio
import json

import aio_pika
import pytest
from aio_pika import connect_robust, Message

from route_calc.model.messages import ResultMessage


@pytest.mark.asyncio
async def test_flow(mock_compute_message_factory):
    with open("./route_calc/config.json", "r", encoding="utf-8") as f:
        cfg = json.load(f)
    connection_string = f"amqp://{cfg['queue']["username"]}:{cfg['queue']['password']}@{cfg['queue']['host']}:{cfg['queue']['port']}"
    connection = await connect_robust(url=connection_string)
    channel = await connection.channel()
    await channel.set_qos(prefetch_count=cfg['queue']['prefetch_count'])
    result_queue = await channel.declare_queue(name=cfg['queue']['results'], durable=True)
    result_pull_based_queue = asyncio.Queue(maxsize=cfg['queue']['prefetch_count'])

    async def handler(message):
        dto = message.body
        r = ResultMessage.from_proto(dto)
        await result_pull_based_queue.put(r)
        await message.ack()

    await result_queue.consume(handler, no_ack=False)

    sent = {}

    for i in range(10):
        payload = mock_compute_message_factory()
        print(f"Sending job {payload.job_id}")
        serialized = payload.to_proto().SerializeToString()
        await channel.default_exchange.publish(
            Message(body=serialized, delivery_mode=aio_pika.DeliveryMode.PERSISTENT),
            routing_key=cfg['queue']['compute'],
        )
        sent[payload.job_id] = True

    while True:
        try:
            rr = await asyncio.wait_for(result_pull_based_queue.get(), timeout=10)
            print(f"Received result {rr.job_id}")
        except asyncio.TimeoutError:
            break

    await channel.close()
    await connection.close()
