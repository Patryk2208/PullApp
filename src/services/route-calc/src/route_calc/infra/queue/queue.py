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

    async def start(self):
        self.connection = await connect_robust(url="TODO")
        self.channel = await self.connection.channel()

    async def consume_job(self) -> Any:
        # has to be [n]acked externally
        msg = await self.channel.get(self.compute_queue, no_ack=False)
        return msg

    async def publish_result(self, result: Any):
        queue = await self.channel.declare_queue(self.result_queue)
        await queue.publish(result)

    async def stop(self):
        await self.connection.close()
