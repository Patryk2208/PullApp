# config.py
import os
import json
from pathlib import Path


def is_running_in_container():
    """Check if running in container"""
    try:
        with open("/proc/1/cgroup", "rt") as f:
            if "docker" in f.read() or "kubepods" in f.read():
                return True
    except Exception:
        pass

    return False


def load_config(path: str = None):
    config_path = path or "/app/config/config.json"

    config = {}
    if Path(config_path).exists():
        with open(config_path) as f:
            config.update(json.load(f))
    print(config)
    config["queue"]["password"] = os.getenv("COMPUTE_QUEUE_PASSWORD")
    config["trip_planner_db"]["password"] = os.getenv("TRIP_PLANNER_DB_PASSWORD")
    config["cache"]["password"] = os.getenv("CACHE_PASSWORD")

    return config