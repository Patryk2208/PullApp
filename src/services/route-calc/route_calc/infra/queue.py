import asyncio
import logging
import socket
from asyncio import CancelledError

import aio_pika
from aio_pika import connect_robust
from aio_pika.abc import AbstractQueue, AbstractRobustChannel, AbstractRobustConnection, AbstractIncomingMessage

from route_calc.api.consumer import JobContext
from route_calc.model.messages import ComputeMessage, ResultMessage


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
    def __init__(self, config: dict, logger: logging.Logger):
        self.config = config
        self.logger = logger
        self.connection: AbstractRobustConnection = None
        self.channel: AbstractRobustChannel = None
        self.compute_queue: AbstractQueue = None
        self.connection_string = f"amqp://{config['username']}:{config['password']}@{config['host']}:{config['port']}"
        self.compute_queue_name = config["compute"]
        self.result_queue_name = config["results"]
        self.timeout_seconds = config["timeout_seconds"]
        self.prefetch_count = config["prefetch_count"]
        self.pull_based_queue: asyncio.Queue[JobContext] = asyncio.Queue(self.prefetch_count)
        self.consumer_tag = None

    async def start(self):
        retries = 0
        while not self.connection and not self.channel and retries < self.config["max_retries"]:
            try:
                self.connection = await connect_robust(url=self.connection_string)
                self.channel = await self.connection.channel()
                await self.channel.set_qos(prefetch_count=self.prefetch_count)
                self.logger.info("Connected to RabbitMQ")
                self.compute_queue = await self.channel.declare_queue(name=self.compute_queue_name, durable=True)
                return
            except (socket.gaierror, ConnectionRefusedError) as e:
                retries += 1
                await asyncio.sleep(2 ** retries)
            except Exception:
                raise BaseQueueException(ConnectionError("Failed to connect to RabbitMQ"))

    async def _make_handler(self):
        async def handler(msg: AbstractIncomingMessage):
            try:
                payload = ComputeMessage.from_proto(msg.body)
            except Exception as e:
                self.logger.warning(
                    "Dropping message with invalid protobuf payload: %s", e
                )
                await msg.nack(requeue=False)
                return

            ctx = JobContext(
                queue_msg=msg,
                payload=payload,
                loop=asyncio.get_running_loop(),
            )
            self.logger.info(f"Received job {ctx.job_id}")
            await self.pull_based_queue.put(ctx)
        return handler


    async def run_consume_job(self):
        # has to be [n]acked externally
        handler = await self._make_handler()
        self.consumer_tag = await self.compute_queue.consume(handler, no_ack=False)

    async def stop_consume_job(self):
        await self.compute_queue.cancel(self.consumer_tag)

    async def ack(self, msg):
        try:
            await msg.ack()
            self.logger.info(f"Acked job {msg.body.job_id}")
        except aio_pika.exceptions.AMQPError:
            pass # already [n]acked or channel closed

    async def nack(self, msg, requeue: bool):
        try:
            await msg.nack(requeue=requeue)
            self.logger.info(f"Nacked job {msg.body.job_id}")
        except aio_pika.exceptions.AMQPError:
            pass # already [n]acked or channel closed

    async def publish_result(self, result: ResultMessage):
        try:
            await self.channel.default_exchange.publish(
                aio_pika.Message(
                    body=result.to_proto().SerializeToString(),
                    delivery_mode=aio_pika.DeliveryMode.PERSISTENT,
                ),
                routing_key=self.result_queue_name,
            )
            self.logger.info(f"Published result for job {result.job_id}")
        except Exception as e:
            raise RequeueException(e)

    async def stop(self):
        await self.connection.close()
        self.logger.info("Connection closed")
