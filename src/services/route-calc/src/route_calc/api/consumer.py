import asyncio
import signal
from asyncio import CancelledError
from concurrent.futures.thread import ThreadPoolExecutor
from typing import Optional

from route_calc.algorithms.algorithms_orchestrator import AlgorithmsOrchestrator
from route_calc.model.messages import ComputeMessage
from route_calc.infra.queue import ComputeQueue, BaseQueueException, RequeueException, NonRequeueException


class JobContext:
    def __init__(self, queue_msg, job_id: int, loop: asyncio.AbstractEventLoop):
        self.msg = queue_msg
        self.job_id = job_id
        self.loop = loop
        self.result = None
        self.future = None
        self.callback_task = None

class Consumer:
    def __init__(self, config: dict, queue: Optional[ComputeQueue], alg_orchestrator: Optional[AlgorithmsOrchestrator]):
        self.config = config
        self.executor = ThreadPoolExecutor(max_workers=self.config["thread_pool"]["max_workers"], thread_name_prefix="route-calc-worker-")
        self.queue = ComputeQueue(self.config["queue"]) if queue is None else queue
        self.algorithms_orchestrator = AlgorithmsOrchestrator(self.config["algorithms"]) if alg_orchestrator is None else alg_orchestrator
        self.loop = asyncio.get_running_loop()

        self.consume_task = None
        self.shutdown_task = None

        self.shutdown_event = asyncio.Event()
        self.should_stop = False
        self.shutdown_timeout = self.config["thread_pool"]["shutdown_timeout_seconds"]

        self.job_key = 0
        self.jobs : dict[int, JobContext] = {}
        #todo configure db

    async def run(self):
        self._setup_signal_handlers()
        await self.queue.start()
        self.shutdown_task = asyncio.create_task(self._wait_for_shutdown())
        while True:
            try:
                self.consume_task = asyncio.create_task(self.queue.consume_job())
                completed, waiting = await asyncio.wait([
                    self.consume_task, self.shutdown_task
                ], return_when=asyncio.FIRST_COMPLETED)
            except BaseQueueException as e:
                print(f"Queue connection error: {e}")
                continue

            if self.should_stop:
                await self._shutdown()
                break
            compute_msg = completed.pop().result()
            ctx = JobContext(compute_msg, self.job_key, self.loop)
            self.jobs[self.job_key] = ctx
            ctx.future = asyncio.wrap_future(self.executor.submit(self._process_job, ctx), loop=self.loop)
            print(
                f"Job {self.job_key} submitted for processing."
                f"Total jobs: {len(self.jobs)}"
            )
            self.job_key += 1

    def _process_job(self, ctx: JobContext):
        dtoPayload = ctx.msg.body
        payload = ComputeMessage.from_proto(dtoPayload)
        result = self.algorithms_orchestrator.compute(payload)
        ctx.result = result

        asyncio.run_coroutine_threadsafe(self._job_done(ctx), self.loop)

    async def _job_done(self, ctx: JobContext):
        ctx.callback_task = asyncio.current_task()
        try:
            result = ctx.result
            await self.queue.publish_result(result)
            await self.queue.ack(ctx.msg)
            _ = self.jobs.pop(ctx.job_id)
            print(f"Job {ctx.job_id} completed. Total jobs: {len(self.jobs)}")
            # todo handle multiple types of exceptions for algorithms
        except RequeueException as e:
            await self.queue.nack(ctx.msg, requeue=True)
            _ = self.jobs.pop(ctx.job_id)
            print(f"Job {ctx.job_id} failed: {e}, requeued")
        except NonRequeueException as e:
            await self.queue.nack(ctx.msg, requeue=False)
            _ = self.jobs.pop(ctx.job_id)
            print(f"Job {ctx.job_id} failed: {e}, not requeued")

    def _setup_signal_handlers(self):
        self.loop.add_signal_handler(signal.SIGINT, self.shutdown_event.set)
        self.loop.add_signal_handler(signal.SIGTERM, self.shutdown_event.set)

    async def _wait_for_shutdown(self):
        await self.shutdown_event.wait()
        self.should_stop = True

    async def _shutdown(self):
        if not self.shutdown_task.done():
            self.shutdown_task.cancel()

        # todo shutdown c++ threads correctly from inside them, not from here, for now we wait until they compute
        self.executor.shutdown(wait=True, cancel_futures=True)

        pending_jobs = [f.callback_task for f in self.jobs.values() if f.callback_task]
        pending_jobs.append(self.consume_task)
        await asyncio.wait(pending_jobs, return_when=asyncio.ALL_COMPLETED)


        # handling the fresh consumed message, we have to nack it
        if not self.consume_task.done():
            self.consume_task.cancel()
        freshly_consumed_msg = None
        try:
            freshly_consumed_msg = await self.consume_task
            await self.queue.nack(freshly_consumed_msg, requeue=True)
        except CancelledError:
            if freshly_consumed_msg:
                await self.queue.nack(freshly_consumed_msg, requeue=True)
        except Exception:
            pass

        # handling the pending callbacks for jobs, we have to nack them
        while self.jobs:
            try:
                f = self.jobs.popitem()[1] # jobs are popped by the callbacks on the event loop, so no race here
                if not f.callback_task.done():
                    f.callback_task.cancel()
                    await self.queue.nack(f.msg, requeue=True)
                    print(f"Callback for job {f.job_id} cancelled")
            except Exception:
                continue
        self.jobs.clear()
        await self.queue.stop()
