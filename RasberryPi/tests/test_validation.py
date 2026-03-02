from datetime import datetime, timezone, timedelta

from raspberry_module.config import AppConfig
from raspberry_module.control import SpeedController
from raspberry_module.models import CommandIn
from raspberry_module.storage import Storage


def test_invalid_speed_falls_back(tmp_path):
    config = AppConfig.load()
    config = config.__class__(
        base_dir=config.base_dir,
        data_dir=tmp_path,
        db_path=tmp_path / "test.db",
        log_path=tmp_path / "test.log",
        speed_min=10.0,
        speed_max=20.0,
        default_speed=15.0,
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

    assert result.status == "invalid"
    assert result.speed_used == 15.0


def test_old_timestamp_invalid(tmp_path):
    config = AppConfig.load()
    config = config.__class__(
        base_dir=config.base_dir,
        data_dir=tmp_path,
        db_path=tmp_path / "test.db",
        log_path=tmp_path / "test.log",
        speed_min=10.0,
        speed_max=20.0,
        default_speed=15.0,
        voltage_min=0.0,
        voltage_max=10.0,
        ramp_rate_v_per_sec=10.0,
        max_timestamp_age_sec=1,
    )

    storage = Storage(config.db_path)
    controller = SpeedController(config, storage)

    command = CommandIn(
        line_id="L1",
        speed=15.0,
        mode="auto",
        timestamp=datetime.now(timezone.utc) - timedelta(seconds=5),
    )
    result = controller.process_command(command)

    assert result.status == "invalid"
