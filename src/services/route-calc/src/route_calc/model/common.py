from dataclasses import dataclass
from enum import Enum
from typing import List
from pydantic import BaseModel, Field, field_validator


class CostType(str, Enum):
    DISTANCE = "distance"
    TIME = "time"
    SCENIC = "scenic"


class AlgorithmType(str, Enum):
    BEST_ROUTE = "best_route"
    CLOSEST_ROUTES = "closest_routes"
    COVERING_ROUTE = "covering_route"
    OPTIMAL_SET = "optimal_set"


class JobStatus(str, Enum):
    PENDING = "pending"
    PROCESSING = "processing"
    SUCCESS = "success"
    FAILED = "failed"
    TIMEOUT = "timeout"


class Point(BaseModel):
    lat: float = Field(..., ge=-90, le=90)
    lon: float = Field(..., ge=-180, le=180)

    @field_validator('lat', 'lon')
    @classmethod
    def validate_coord(cls, v):
        if v is None:
            raise ValueError("Coordinate cannot be None")
        return v

    class Config:
        frozen = True  # Immutable, usable as dict key