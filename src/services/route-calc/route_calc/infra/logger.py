import logging

def setup_logging(config: dict) -> logging.Logger:
    logger = logging.getLogger()
    logger.setLevel(config["level"])
    f = "%(asctime)s | %(levelname)s | %(name)s | %(message)s"
    d = "%Y-%m-%d %H:%M:%S"
    logging.basicConfig(
        format=f,
        datefmt=d
    )
    return logger