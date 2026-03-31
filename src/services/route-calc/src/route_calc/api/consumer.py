import signal

from route_calc.infra.queue.queue import ComputeQueue

class Consumer:
    def __init__(self, q: ComputeQueue):
        pass

    async def consume(self):
        # todo, run consumer, consume job, run algorithm synchronously, publish result
        # todo handle exceptions and cancellations
        pass