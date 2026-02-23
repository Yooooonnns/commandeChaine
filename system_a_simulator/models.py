"""Pydantic models for System A API."""
from datetime import datetime
from typing import Optional, List
from pydantic import BaseModel, Field

class BatchInputRequest(BaseModel):
    line_id: str = Field(min_length=1)
    production_times: List[float] = Field(min_length=1)
    worker_count: int = Field(ge=1)
    productivity_factor: float = Field(default=1.0, gt=0)

class ManualCtRequest(BaseModel):
    line_id: str = Field(min_length=1)
    calculated_ct_seconds: float = Field(gt=0)
    chain_state: Optional[dict] = Field(default=None)
    jigs: Optional[List[str]] = Field(default=None)

class CtPublishResponse(BaseModel):
    status: str
    line_id: str
    calculated_ct_seconds: float
    calculated_ct_minutes: float
    timestamp: datetime
    reason: str

class HealthResponse(BaseModel):
    status: str
    mqtt_connected: bool
    timestamp: datetime
