import json

from route_calc.algorithms.algorithms_orchestrator import AlgorithmsOrchestrator
from route_calc.api.consumer import Consumer
from route_calc.infra.logger import setup_logging
from route_calc.infra.queue import ComputeQueue

if __name__ == '__main__':
    with open("config.json", "r", encoding="utf-8") as f:
        cfg = json.load(f)
    logger = setup_logging(cfg["logging"])
    logger.info("Starting route-calc")
    a_o = AlgorithmsOrchestrator(config=cfg["algorithms"], logger=logger)
    q = ComputeQueue(config=cfg["queue"], logger=logger)
    c = Consumer(config=cfg, queue=q, alg_orchestrator=a_o, logger=logger)
    c.run()
    logger.info("Exiting route-calc")