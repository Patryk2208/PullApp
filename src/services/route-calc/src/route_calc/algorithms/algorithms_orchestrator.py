import logging

from route_calc.model.messages import ComputeMessage, ResultMessage


class AlgorithmsOrchestrator:
    def __init__(self, config: dict, logger: logging.Logger):
        self.config = config
        self.logger = logger

    def compute(self, payload: ComputeMessage) -> ResultMessage:
        pass