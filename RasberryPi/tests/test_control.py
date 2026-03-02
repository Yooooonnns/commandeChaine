from datetime import datetime, timezone

from raspberry_module.config import AppConfig
from raspberry_module.control import SpeedController
from raspberry_module.models import CommandIn
from raspberry_module.storage import Storage


def test_speed_to_voltage_and_ramp(tmp_path):
    config = AppConfig.load()
    config = config.__class__(
        base_dir=config.base_dir,
        data_dir=tmp_path,
        db_path=tmp_path / "test.db",
        log_path=tmp_path / "test.log",
        speed_min=0.0,
        speed_max=100.0,
        default_speed=50.0,
        voltage_min=0.0,
        voltage_max=10.0,
        ramp_rate_v_per_sec=10.0,
        max_timestamp_age_sec=60,
    )

    storage = Storage(config.db_path)
    controller = SpeedController(config, storage)

    command = CommandIn(
        line_id="L1",
        speed=100.0,
        mode="auto",
        timestamp=datetime.now(timezone.utc),
    )
    result = controller.process_command(command)

    assert result.voltage == 10.0
    assert result.speed_used == 100.0


def test_cycle_time_moving_average_and_no_ramp(tmp_path):
    config = AppConfig.load()
    config = config.__class__(
        base_dir=config.base_dir,
        data_dir=tmp_path,
        db_path=tmp_path / "test.db",
        log_path=tmp_path / "test.log",
        speed_min=0.0,
        speed_max=100.0,
        default_speed=50.0,
        voltage_min=0.0,
        voltage_max=10.0,
        ramp_rate_v_per_sec=0.01,
        max_timestamp_age_sec=60,
        ct_filter_window_samples=3,
    )

    storage = Storage(config.db_path)
    controller = SpeedController(config, storage)

    result1 = controller.process_cycle_time("L1", cycle_time_minutes=2.0, mode="mqtt")
    result2 = controller.process_cycle_time("L1", cycle_time_minutes=1.0, mode="mqtt")

    assert controller.last_filtered_cycle_time == 1.5
    assert result2.speed_used > result1.speed_used

    manual = CommandIn(
        line_id="L1",
        speed=100.0,
        mode="manual",
        timestamp=datetime.now(timezone.utc),
    )
    manual_result = controller.process_command(manual)
    assert manual_result.voltage == 10.0
