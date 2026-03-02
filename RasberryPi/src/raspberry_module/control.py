from dataclasses import dataclass
from collections import deque
from datetime import datetime, timezone
from typing import Optional, Tuple

import logging

from .config import AppConfig
from .models import CommandIn
from .storage import Storage

logger = logging.getLogger(__name__)


@dataclass
class ControlResult:
    status: str
    speed_used: float
    voltage: float
    reason: str
    applied_at: datetime


@dataclass
class ChainStateSnapshot:
    is_running: Optional[bool]
    encoder_delta: Optional[float]
    updated_at: datetime


class SpeedController:
    def __init__(self, config: AppConfig, storage: Storage) -> None:
        self._config = config
        self._storage = storage
        self._last_valid_speed: Optional[float] = None
        self._last_voltage: Optional[float] = None
        self._last_output_time: Optional[datetime] = None
        self._ct_history: deque[float] = deque(maxlen=max(1, config.ct_filter_window_samples))
        self._last_filtered_cycle_time: Optional[float] = None
        self._last_chain_state: Optional[ChainStateSnapshot] = None

    @property
    def last_valid_speed(self) -> Optional[float]:
        return self._last_valid_speed

    @property
    def last_voltage(self) -> Optional[float]:
        return self._last_voltage

    @property
    def last_filtered_cycle_time(self) -> Optional[float]:
        return self._last_filtered_cycle_time

    @property
    def last_chain_state(self) -> Optional[ChainStateSnapshot]:
        return self._last_chain_state

    def process_command(self, command: CommandIn) -> ControlResult:
        received_at = datetime.now(timezone.utc)
        status, reason = self._validate_command(command, received_at)

        if status == "valid":
            self._last_valid_speed = command.speed
            speed_used = command.speed
        else:
            speed_used = self._last_valid_speed
            if speed_used is None:
                speed_used = self._config.default_speed

        target_voltage = self._speed_to_voltage(speed_used)
        applied_voltage = target_voltage
        self._last_voltage = applied_voltage
        self._last_output_time = received_at

        self._storage.log_command(
            received_at=received_at,
            line_id=command.line_id,
            speed=command.speed,
            mode=command.mode,
            timestamp=command.timestamp,
            status=status,
            reason=reason,
            raw_json=command.model_dump(mode="json"),
        )
        self._storage.log_output(
            created_at=received_at,
            speed_used=speed_used,
            voltage=applied_voltage,
            reason=reason,
        )

        return ControlResult(
            status=status,
            speed_used=speed_used,
            voltage=applied_voltage,
            reason=reason,
            applied_at=received_at,
        )

    def process_cycle_time(
        self,
        line_id: str,
        cycle_time_minutes: float,
        mode: str = "mqtt",
        chain_state: Optional[dict] = None,
    ) -> ControlResult:
        if cycle_time_minutes <= 0:
            raise ValueError("cycle_time_minutes must be > 0")

        if chain_state is not None:
            self._update_chain_state(chain_state)

        filtered_cycle_time = self._filter_cycle_time(cycle_time_minutes)
        self._last_filtered_cycle_time = filtered_cycle_time

        speed = (self._config.ct_to_speed_factor / filtered_cycle_time)
        speed = max(self._config.speed_min, min(self._config.speed_max, speed))

        command = CommandIn(
            line_id=line_id,
            speed=speed,
            mode=mode,
            timestamp=datetime.now(timezone.utc),
        )
        return self.process_command(command)

    def _filter_cycle_time(self, cycle_time_minutes: float) -> float:
        self._ct_history.append(cycle_time_minutes)
        if not self._ct_history:
            return cycle_time_minutes
        return sum(self._ct_history) / len(self._ct_history)

    def _update_chain_state(self, chain_state: dict) -> None:
        is_running_raw = chain_state.get("is_running")
        encoder_delta_raw = chain_state.get("encoder_delta")

        is_running = bool(is_running_raw) if is_running_raw is not None else None
        encoder_delta = float(encoder_delta_raw) if encoder_delta_raw is not None else None

        self._last_chain_state = ChainStateSnapshot(
            is_running=is_running,
            encoder_delta=encoder_delta,
            updated_at=datetime.now(timezone.utc),
        )

    def _validate_command(self, command: CommandIn, received_at: datetime) -> Tuple[str, str]:
        if command.speed < self._config.speed_min or command.speed > self._config.speed_max:
            return "invalid", "speed_out_of_range"

        if self._config.max_timestamp_age_sec > 0:
            age = received_at - command.timestamp.astimezone(timezone.utc)
            if age.total_seconds() > self._config.max_timestamp_age_sec:
                return "invalid", "timestamp_too_old"

        return "valid", "ok"

    def _speed_to_voltage(self, speed: float) -> float:
        span = self._config.speed_max - self._config.speed_min
        if span <= 0:
            return self._config.voltage_min

        ratio = (speed - self._config.speed_min) / span
        ratio = max(0.0, min(1.0, ratio))
        return self._config.voltage_min + ratio * (
            self._config.voltage_max - self._config.voltage_min
        )

    def _apply_ramp(self, target_voltage: float, now: datetime) -> float:
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
