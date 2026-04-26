import asyncio
import json
import subprocess
import time

import aio_pika
import pytest
from aio_pika import connect_robust, Message

from route_calc.model.messages import ResultMessage

@pytest.mark.asyncio
async def test_flow(mock_compute_message_factory):
    with open("./route_calc/generated/config.json", "r", encoding="utf-8") as f:
        cfg = json.load(f)
    connection_string = f"amqp://{cfg['queue']["username"]}:{cfg['queue']['password']}@{cfg['queue']['host']}:{cfg['queue']['port']}"
    connection = await connect_robust(url=connection_string)
    channel = await connection.channel()
    await channel.set_qos(prefetch_count=cfg['queue']['prefetch_count'])

    # msg_count = 50
    # for i in range(msg_count):
    while True:
        payload = mock_compute_message_factory()
        print(f"Sending job {payload.job_id}")
        serialized = payload.to_proto().SerializeToString()
        await channel.default_exchange.publish(
            Message(body=serialized, delivery_mode=aio_pika.DeliveryMode.PERSISTENT),
            routing_key=cfg['queue']['compute'],
        )
        time.sleep(0.1)

    await channel.close()
    await connection.close()