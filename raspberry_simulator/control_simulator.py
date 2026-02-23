"""Speed controller simulator for System B."""
from dataclasses import dataclass
from collections import deque
from datetime import datetime, timezone
from typing import Optional
import logging

logger = logging.getLogger(__name__)

@dataclass
class ChainStateSnapshot:
    is_running: Optional[bool]
    encoder_delta: Optional[float]
    updated_at: datetime

class SpeedControllerSimulator:
    def __init__(self, config, database):
        self._config = config
        self._database = database
        self._last_valid_speed = None
        self._last_voltage = None
        self._last_output_time = None
        self._ct_history = deque(maxlen=max(1, config.ct_filter_window_samples))
        self._last_filtered_cycle_time = None
        self._last_chain_state = None
    
    @property
    def last_valid_speed(self):
        return self._last_valid_speed
    
    @property
    def last_voltage(self):
        return self._last_voltage
    
    @property
    def last_filtered_cycle_time(self):
        return self._last_filtered_cycle_time
    
    @property
    def last_chain_state(self):
        return self._last_chain_state
    
    def process_cycle_time(self, line_id, cycle_time_minutes, chain_state=None):
        if cycle_time_minutes <= 0:
            raise ValueError("cycle_time_minutes must be > 0")
        if chain_state is not None:
            self._update_chain_state(chain_state)
        filtered_cycle_time = self._filter_cycle_time(cycle_time_minutes)
        self._last_filtered_cycle_time = filtered_cycle_time
        speed = self._cycle_time_to_speed(filtered_cycle_time)
        speed = max(self._config.speed_min, min(self._config.speed_max, speed))
        self._last_valid_speed = speed
        now = datetime.now(timezone.utc)
        target_voltage = self._speed_to_voltage(speed)
        applied_voltage = self._apply_ramp(target_voltage, now)
        self._last_voltage = applied_voltage
        self._last_output_time = now
        try:
            self._database.save_control_log(line_id=line_id, ct_seconds=cycle_time_minutes * 60.0, filtered_ct_seconds=filtered_cycle_time * 60.0, voltage=applied_voltage, speed=speed, timestamp=now)
        except Exception as exc:
            logger.warning("Failed to log control result: %s", exc)
        logger.info("Processed CT - line=%s speed=%.1f voltage=%.2f", line_id, speed, applied_voltage)
        return {"status": "valid", "speed_used": speed, "voltage": applied_voltage, "filtered_ct_seconds": filtered_cycle_time * 60.0, "reason": "ok", "applied_at": now}
    
    def _filter_cycle_time(self, cycle_time_minutes):
        self._ct_history.append(cycle_time_minutes)
        if not self._ct_history:
            return cycle_time_minutes
        return sum(self._ct_history) / len(self._ct_history)
    
    def _update_chain_state(self, chain_state):
        is_running = bool(chain_state.get("is_running")) if chain_state.get("is_running") is not None else None
        encoder_delta = float(chain_state.get("encoder_delta")) if chain_state.get("encoder_delta") is not None else None
        self._last_chain_state = ChainStateSnapshot(is_running=is_running, encoder_delta=encoder_delta, updated_at=datetime.now(timezone.utc))
    
    def _cycle_time_to_speed(self, cycle_time_minutes):
        if cycle_time_minutes <= 0:
            return self._config.default_speed
        return self._config.ct_to_speed_factor / cycle_time_minutes
    
    def _speed_to_voltage(self, speed):
        span = self._config.speed_max - self._config.speed_min
        if span <= 0:
            return self._config.voltage_min
        ratio = (speed - self._config.speed_min) / span
        ratio = max(0.0, min(1.0, ratio))
        return self._config.voltage_min + ratio * (self._config.voltage_max - self._config.voltage_min)
    
    def _apply_ramp(self, target_voltage, now):
        if self._last_voltage is None or self._last_output_time is None:
            self._last_voltage = target_voltage
            self._last_output_time = now
            return target_voltage
        dt = (now - self._last_output_time).total_seconds()
        if dt <= 0:
            self._last_voltage = target_voltage
            self._last_output_time = now
            return target_voltage
        max_step = max(0.0, self._config.ramp_rate_v_per_sec) * dt
        delta = target_voltage - self._last_voltage
        if abs(delta) > max_step:
            delta = max_step if delta > 0 else -max_step
        self._last_voltage = self._last_voltage + delta
        self._last_output_time = now
        return self._last_voltage
