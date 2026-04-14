import logging

def setup_logging(config: dict) -> logging.Logger:
    logger = logging.getLogger()
    logger.setLevel(config["level"])
    logging.basicConfig(
        format=config["format"],
        datefmt=config["datefmt"]
    )
    return logger