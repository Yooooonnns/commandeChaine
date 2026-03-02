"""Heijunka CT calculation formulas."""
from typing import Sequence

def calculate_ct(production_times: Sequence[float], worker_count: int, productivity_factor: float = 1.0) -> float:
    if not production_times or len(production_times) == 0:
        raise ValueError("production_times must not be empty")
    if worker_count < 1:
        raise ValueError("worker_count must be at least 1")
    if productivity_factor <= 0:
        raise ValueError("productivity_factor must be positive")
    
    mean_time = sum(production_times) / len(production_times)
    ct_minutes = mean_time / max(1, worker_count) / productivity_factor
    return ct_minutes

def ct_to_seconds(ct_minutes: float) -> float:
    if ct_minutes <= 0:
        raise ValueError("ct_minutes must be positive")
    return ct_minutes * 60.0

def seconds_to_ct(ct_seconds: float) -> float:
    if ct_seconds <= 0:
        raise ValueError("ct_seconds must be positive")
    return ct_seconds / 60.0
