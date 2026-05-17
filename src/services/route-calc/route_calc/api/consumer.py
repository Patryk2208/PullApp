import asyncio
import logging
import signal
from concurrent.futures.thread import ThreadPoolExecutor
from typing import Optional

from opentelemetry import metrics, trace

from route_calc.algorithms.algorithms_orchestrator import AlgorithmsOrchestrator
from route_calc.model.job_context import JobContext
from route_calc.infra.queue import ComputeQueue, BaseQueueException, RequeueException, NonRequeueException

_tracer = trace.get_tracer(__name__)
_meter = metrics.get_meter(__name__)
_jobs_processed = _meter.create_counter("route_calc.jobs.processed", description="Total compute jobs processed")
_jobs_failed = _meter.create_counter("route_calc.jobs.failed", description="Total compute jobs failed")


class Consumer:
    def __init__(self, config: dict, logger: logging.Logger, queue: Optional[ComputeQueue], alg_orchestrator: Optional[AlgorithmsOrchestrator]):
        self.config = config
        self.logger = logger
        self.executor = ThreadPoolExecutor(max_workers=self.config["thread_pool"]["max_workers"], thread_name_prefix="route-calc-worker-")
        self.queue = ComputeQueue(self.config["queue"]) if queue is None else queue
        self.algorithms_orchestrator = AlgorithmsOrchestrator(self.config["algorithms"]) if alg_orchestrator is None else alg_orchestrator
        self.loop = asyncio.get_event_loop() if asyncio.get_event_loop() else asyncio.new_event_loop()

        self.consume_task = None
        self.shutdown_task = None

        self.shutdown_event = asyncio.Event()
        self.shutdown_lock = asyncio.Lock()
        self.should_stop = False
        self.shutdown_timeout = self.config["thread_pool"]["shutdown_timeout_seconds"]

        self.jobs : dict[str, JobContext] = {}
        #todo configure db

    async def run(self):
        self._setup_signal_handlers()
        await self.queue.start()
        await self.queue.run_consume_job()
        self.shutdown_task = asyncio.create_task(self._wait_for_shutdown())
        while True:
            self.consume_task = asyncio.create_task(self.queue.pull_based_queue.get())
            completed, waiting = await asyncio.wait([
                self.consume_task, self.shutdown_task
            ], return_when=asyncio.FIRST_COMPLETED)

            if self.should_stop:
                await self._shutdown()
                break
            ctx = completed.pop().result()
            self.logger.info(f"Consumed job from queue: {ctx.job_id}")
            self.jobs[ctx.job_id] = ctx
            fut = asyncio.wrap_future(self.executor.submit(self._process_job, ctx), loop=self.loop)
            ctx.useless_mutex.acquire_lock()
            ctx.future = fut
            ctx.useless_mutex.release_lock()
            self.logger.info(f"Job {ctx.job_id} submitted for processing. Total jobs: {len(self.jobs)}")

    def _process_job(self, ctx: JobContext):
        ctx.useless_mutex.acquire_lock()
        ctx.future = None
        ctx.useless_mutex.release_lock()

        self.logger.info(f"Processing job {ctx.job_id}")
        with _tracer.start_as_current_span("route_calc.compute", attributes={"job.id": ctx.job_id}):
            try:
                result = self.algorithms_orchestrator.compute(ctx.payload)
                _jobs_processed.add(1)
            except Exception:
                _jobs_failed.add(1)
                raise
        self.logger.info(f"Job {ctx.job_id} processed, now scheduling callback")
        ctx.result = result

        fut = asyncio.run_coroutine_threadsafe(self._job_done(ctx), self.loop)
        ctx.useless_mutex.acquire_lock()
        ctx.scheduling_future = fut
        ctx.useless_mutex.release_lock()

    async def _job_done(self, ctx: JobContext):
        ctx.useless_mutex.acquire_lock()
        ctx.scheduling_future = None
        ctx.useless_mutex.release_lock()

        ctx.callback_task = asyncio.current_task()
        try:
            result = ctx.result
            await self.queue.publish_result(result)
            await self.queue.ack(ctx.msg)
            self.logger.info(f"Job {ctx.job_id} acked")
            await self.shutdown_lock.acquire()
            _ = self.jobs.pop(ctx.job_id)
            self.shutdown_lock.release()
            self.logger.info(f"Job {ctx.job_id} completed. Total jobs: {len(self.jobs)}")
            # todo handle multiple types of exceptions for algorithms
        except RequeueException as e:
            await self.queue.nack(ctx.msg, requeue=True)
            self.logger.info(f"Job {ctx.job_id} nacked")
            await self.shutdown_lock.acquire()
            _ = self.jobs.pop(ctx.job_id)
            self.shutdown_lock.release()
            self.logger.info(f"Job {ctx.job_id} failed: {e}, requeued")
        except NonRequeueException as e:
            await self.queue.nack(ctx.msg, requeue=False)
            self.logger.info(f"Job {ctx.job_id} nacked (not requeued)")
            await self.shutdown_lock.acquire()
            _ = self.jobs.pop(ctx.job_id)
            self.shutdown_lock.release()
            self.logger.info(f"Job {ctx.job_id} failed: {e}, not requeued")

    def _setup_signal_handlers(self):
        self.loop.add_signal_handler(signal.SIGINT, self.shutdown_event.set)
        self.loop.add_signal_handler(signal.SIGTERM, self.shutdown_event.set)

    async def _wait_for_shutdown(self):
        await self.shutdown_event.wait()
        self.should_stop = True

    async def _shutdown(self):
        self.logger.info("Shutting down...")
        if not self.shutdown_task.done():
            self.shutdown_task.cancel()

        # handling the queue polling
        await self.queue.stop_consume_job()

        # handling the freshly consumed message, we have to nack it
        while self.queue.pull_based_queue.qsize() > 0:
            fresh = self.queue.pull_based_queue.get_nowait()
            try:
                await self.queue.nack(fresh.msg, requeue=True)
            except Exception:
                continue

        # handling the jobs submitted for processing, but not yet scheduled
        await self.shutdown_lock.acquire()
        for j in self.jobs.values():
            if j.future:
                j.future.cancel()
                try:
                    await self.queue.nack(j.msg, requeue=True)
                except Exception:
                    continue
                self.logger.info(f"Executor submission for job {j.job_id} cancelled")
        self.shutdown_lock.release()

        # todo shutdown c++ threads correctly from inside them, not from here, for now we wait until they compute
        self.logger.info("Waiting for all jobs to complete...")
        self.executor.shutdown(wait=True, cancel_futures=True)

        # handling the pending callbacks for jobs, we have to nack them
        await self.shutdown_lock.acquire()
        for j in self.jobs.values():
            if j.scheduling_future:
                j.scheduling_future.cancel()
                try:
                    await self.queue.nack(j.msg, requeue=True)
                except Exception:
                    continue
                self.logger.info(f"Post compute scheduling for job {j.job_id} cancelled")
        self.shutdown_lock.release()

        pending_jobs = [f.callback_task for f in self.jobs.values() if f.callback_task]
        if pending_jobs:
            await asyncio.wait(pending_jobs, return_when=asyncio.ALL_COMPLETED)
        while self.jobs:
            try:
                f = self.jobs.popitem()[1] # jobs are popped by the callbacks on the event loop, so no race here
                if not f.callback_task.done():
                    f.callback_task.cancel()
                    try:
                        await self.queue.nack(f.msg, requeue=True)
                    except Exception:
                        continue
                    self.logger.info(f"Callback for job {f.job_id} cancelled")
            except Exception:
                continue

        self.jobs.clear()
        self.logger.info("All jobs completed or nacked")
        await self.queue.stop()
