from typing import Callable, Coroutine, Any

from aio_pika import connect_robust


class ComputeQueue:
    def __init__(self, config: dict):
        self.config = config
        self.connection = None
        self.channel = None
        self.connection_string = "TODO"
        self.compute_queue = config["queues"]["compute"]
        self.result_queue = config["queues"]["results"]

    async def __aenter__(self):
        self.connection = await connect_robust(url="TODO")
        self.channel = await self.connection.channel()

    async def consume_job(self, callback: Callable[[Any], Coroutine]) -> Any:
        queue = await self.channel.declare_queue(self.compute_queue)
        result = None
        async with queue.iterator() as queue_iter:
            async for message in queue_iter:
                # todo handle conversion with validation
                result = await callback(message) # todo strongly typed callback
                # todo handle exceptions
                await message.ack()
        return result

    async def publish_result(self, result: Any):
        queue = await self.channel.declare_queue(self.result_queue)
        await queue.publish(result)

    def __aexit__(self, exc_type, exc_val, exc_tb):
        self.connection.close()
