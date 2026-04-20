import asyncio
import threading

from route_calc.model.messages import ComputeMessage


class JobContext:
    def __init__(self, queue_msg, payload: ComputeMessage, loop: asyncio.AbstractEventLoop):
        self.msg = queue_msg
        self.payload = payload
        self.job_id = self.payload.job_id
        self.loop = loop
        self.result = None
        self.future = None
        self.callback_task = None
        self.useless_mutex = threading.Lock()
        self.scheduling_future = None
