import asyncio

from route_calc.algorithms.algorithms_orchestrator import AlgorithmsOrchestrator
from route_calc.api.consumer import Consumer
from route_calc.infra.config import load_config
from route_calc.infra.health import start_health_server
from route_calc.infra.logger import setup_logging
from route_calc.infra.queue import ComputeQueue
from route_calc.infra.telemetry import setup_telemetry

async def main():
    cfg = load_config()
    logger = setup_logging(cfg["logging"])
    setup_telemetry()
    logger.info("Starting route-calc")
    await start_health_server()
    a_o = AlgorithmsOrchestrator(config=cfg["algorithms"], logger=logger)
    q = ComputeQueue(config=cfg["queue"], logger=logger)
    c = Consumer(config=cfg, queue=q, alg_orchestrator=a_o, logger=logger)
    try:
        await c.run()
    except:
        logger.exception("Unexpected error")
    logger.info("Exiting route-calc")

if __name__ == '__main__':
    asyncio.run(main())
