from datetime import datetime
from pydantic import BaseModel, Field


class CommandIn(BaseModel):
    line_id: str = Field(min_length=1)
    speed: float
    mode: str = Field(min_length=1)
    timestamp: datetime


class CommandOut(BaseModel):
    status: str
    speed_used: float
    voltage: float
    reason: str
    applied_at: datetime
