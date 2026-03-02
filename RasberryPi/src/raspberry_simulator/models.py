"""Pydantic models for System B API."""
from datetime import datetime
from typing import Optional
from pydantic import BaseModel, Field

class ManualCommandRequest(BaseModel):
    line_id: str = Field(min_length=1)
    speed: float = Field(ge=0)
    mode: str = Field(default="manual")

class ControlResultResponse(BaseModel):
    status: str
    line_id: str
    speed_used: float
    voltage: float
    filtered_ct_seconds: float
    timestamp: datetime

class StateResponse(BaseModel):
    last_valid_speed: Optional[float] = None
    last_voltage: Optional[float] = None
    last_filtered_cycle_time: Optional[float] = None
    chain_state: Optional[dict] = None
    timestamp: datetime

class HealthResponse(BaseModel):
    status: str
    mqtt_connected: bool
    api_enabled: bool
    timestamp: datetime
