import asyncio
import socket
from typing import Any

import aio_pika
from aio_pika import connect_robust


class BaseQueueException(Exception):
    def __init__(self, e: Exception):
        super().__init__(e)


class RequeueException(BaseQueueException):
    def __init__(self, e: Exception):
        super().__init__(e)


class NonRequeueException(BaseQueueException):
    def __init__(self, e: Exception):
        super().__init__(e)


class ComputeQueue:
    def __init__(self, config: dict):
        self.config = config
        self.connection = None
        self.channel = None
        self.connection_string = "TODO"
        self.compute_queue = config["queues"]["compute"]
        self.result_queue = config["queues"]["results"]

    async def start(self):
        retries = 0
        while not self.connection and not self.channel and retries < self.config["max_retries"]:
            try:
                self.connection = await connect_robust(url="TODO")
                self.channel = self.connection.channel()
                await self.channel.set_qos(prefetch_count=self.config["prefetch_count"])
                return
            except (socket.gaierror, ConnectionRefusedError) as e:
                retries += 1
                await asyncio.sleep(2 ** retries)
        raise BaseQueueException(ConnectionError("Failed to connect to RabbitMQ"))


    async def consume_job(self) -> Any:
        # has to be [n]acked externally
        try:
            msg = await self.channel.get(self.compute_queue, no_ack=False)
            return msg
        except aio_pika.exceptions.ChannelInvalidStateError:
            await self.start()
            return await self.consume_job()
        except asyncio.CancelledError:
            return None

    async def ack(self, msg):
        try:
            await msg.ack()
        except aio_pika.exceptions.AMQPError:
            pass # already [n]acked or channel closed

    async def nack(self, msg, requeue):
        try:
            await msg.nack(requeue=requeue)
        except aio_pika.exceptions.AMQPError:
            pass # already [n]acked or channel closed

    async def publish_result(self, result: Any):
        try:
            queue = await self.channel.declare_queue(self.result_queue)
            await queue.publish(result)
        except Exception as e:
            raise RequeueException(e)

    async def stop(self):
        await self.connection.close()
