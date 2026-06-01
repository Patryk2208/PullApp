"""One-shot queue purge before route-calc starts (used by docker-compose queue-setup)."""
import asyncio

from tests.rabbitmq_e2e_test import purge_queues


if __name__ == "__main__":
    asyncio.run(purge_queues())
    print("Purged compute-queue and results-queue")
