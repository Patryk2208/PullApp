from route_calc.model.messages import ComputeMessage, ResultMessage


class AlgorithmsOrchestrator:
    def __init__(self, config: dict):
        self.config = config

    def compute(self, payload: ComputeMessage) -> ResultMessage:
        pass