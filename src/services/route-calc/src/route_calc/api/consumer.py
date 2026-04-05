import asyncio
import signal
from concurrent.futures.thread import ThreadPoolExecutor

from route_calc.algorithms.mock_algorithm import mock_algorithm
from route_calc.infra.queue import ComputeQueue, BaseQueueException, RequeueException, NonRequeueException


class JobFutureWithContext:
    def __init__(self, future: asyncio.Future, queue_msg, job_id: int, callback):
        self.future = future
        self.msg = queue_msg
        self.job_id = job_id
        self.callback = callback
        self.future.add_done_callback(self._on_complete)

    async def _on_complete(self, future):
        if self.callback:
            self.callback(self)

class Consumer:
    def __init__(self, config: dict):
        self.config = config
        self.executor = ThreadPoolExecutor(self.config["thread_pool"])
        self.queue = ComputeQueue(self.config["queue"])

        self.consume_task = None
        self.shutdown_task = None

        self.should_stop = False
        self.shutdown_timeout = self.config["thread_pool"]["shutdown_timeout_seconds"]

        self.job_key = 0
        self.jobs : dict[int, JobFutureWithContext] = {}
        #todo configure db and cache

    async def run(self):
        await self.queue.start()
        while True:
            try:
                self.consume_task = asyncio.create_task(self.queue.consume_job())
                self.shutdown_task = asyncio.create_task(self.wait_for_shutdown())
                completed, waiting = await asyncio.wait([
                    self.consume_task, self.shutdown_task
                ], return_when=asyncio.FIRST_COMPLETED)
            except BaseQueueException as e:
                print(f"Queue connection error: {e}")
                continue

            if self.should_stop:
                await self.shutdown()
                break
            else:
                compute_msg = completed.pop().result()
                self.jobs[self.job_key] = JobFutureWithContext(
                    self.executor.submit(self.process_job(msg=compute_msg)),
                    queue_msg=compute_msg,
                    job_id=self.job_key,
                    callback=self.job_done
                )
                print(
                    f"Job {self.job_key} submitted for processing."
                    f"Total jobs: {len(self.jobs)}"
                )
                self.job_key += 1

    @staticmethod
    def process_job(msg):
        # todo full algorithm mechanics here maybe extract this elsewhere
        payload = msg.body
        result = mock_algorithm(payload)
        return result

    async def job_done(self, jf: JobFutureWithContext):
        try:
            result = jf.future.result()
            await self.queue.publish_result(result)
            await self.queue.ack(jf.msg)
            _ = self.jobs.pop(jf.job_id)
            print(f"Job {jf.job_id} completed. Total jobs: {len(self.jobs)}")

            # todo handle multiple types of exceptions for algorithms
        except RequeueException as e:
            await self.queue.nack(jf.msg, requeue=True)
            _ = self.jobs.pop(jf.job_id)
            print(f"Job {jf.job_id} failed: {e}, requeued")
        except NonRequeueException as e:
            await self.queue.nack(jf.msg, requeue=False)
            _ = self.jobs.pop(jf.job_id)
            print(f"Job {jf.job_id} failed: {e}, not requeued")


    async def wait_for_shutdown(self):
        signal.sigwait([signal.SIGINT, signal.SIGTERM])
        self.should_stop = True

    async def shutdown(self):
        self.consume_task.cancel()
        pending_jobs = [f.future for f in self.jobs.values()]
        await asyncio.wait(pending_jobs, timeout=self.shutdown_timeout, return_when=asyncio.ALL_COMPLETED)
        for f in self.jobs.values():
            if not f.future.done():
                f.future.cancel()
                await f.msg.nack(requeue=True)
        self.jobs.clear()
        self.executor.shutdown(wait=False)
        await self.queue.stop()
