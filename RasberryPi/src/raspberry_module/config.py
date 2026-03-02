from dataclasses import dataclass
from pathlib import Path
import os


def _get_float(name: str, default: float) -> float:
    value = os.getenv(name)
    if value is None or value.strip() == "":
        return default
    return float(value)


def _get_int(name: str, default: int) -> int:
    value = os.getenv(name)
    if value is None or value.strip() == "":
        return default
    return int(value)


@dataclass(frozen=True)
class AppConfig:
    base_dir: Path
    data_dir: Path
    db_path: Path
    log_path: Path
    speed_min: float
    speed_max: float
    default_speed: float
    voltage_min: float
    voltage_max: float
    ramp_rate_v_per_sec: float
    max_timestamp_age_sec: int
    ct_filter_window_samples: int = 5
    mqtt_enabled: bool = True
    mqtt_host: str = "localhost"
    mqtt_port: int = 1883
    mqtt_topic: str = "yazaki/line/+/ct"
    mqtt_speed_response_topic: str = "yazaki/line/{line_id}/speed"
    ct_to_speed_factor: float = 1.0

    @classmethod
    def load(cls) -> "AppConfig":
        base_dir = Path(__file__).resolve().parents[2]
        data_dir = base_dir / "data"
        data_dir.mkdir(parents=True, exist_ok=True)

        db_path = Path(os.getenv("RASPI_DB_PATH", data_dir / "raspberry.db"))
        log_path = Path(os.getenv("RASPI_LOG_PATH", data_dir / "raspberry.log"))

        speed_min = _get_float("RASPI_SPEED_MIN", 20.0)
        speed_max = _get_float("RASPI_SPEED_MAX", 80.0)
        default_speed = _get_float("RASPI_DEFAULT_SPEED", 50.0)
        voltage_min = _get_float("RASPI_VOLTAGE_MIN", 0.0)
        voltage_max = _get_float("RASPI_VOLTAGE_MAX", 10.0)
        ramp_rate_v_per_sec = _get_float("RASPI_RAMP_RATE_V_PER_SEC", 1.0)
        max_timestamp_age_sec = _get_int("RASPI_MAX_TIMESTAMP_AGE_SEC", 30)
        ct_filter_window_samples = max(1, _get_int("RASPI_CT_FILTER_WINDOW_SAMPLES", 5))
        mqtt_enabled = _get_bool("RASPI_MQTT_ENABLED", True)
        mqtt_host = os.getenv("RASPI_MQTT_HOST", "localhost")
        mqtt_port = _get_int("RASPI_MQTT_PORT", 1883)
        mqtt_topic = os.getenv("RASPI_MQTT_TOPIC", "yazaki/line/+/ct")
        mqtt_speed_response_topic = os.getenv("RASPI_MQTT_SPEED_RESPONSE_TOPIC", "yazaki/line/{line_id}/speed")
        ct_to_speed_factor = _get_float("RASPI_CT_TO_SPEED_FACTOR", 1.0)

        return cls(
            base_dir=base_dir,
            data_dir=data_dir,
            db_path=db_path,
            log_path=log_path,
            speed_min=speed_min,
            speed_max=speed_max,
            default_speed=default_speed,
            voltage_min=voltage_min,
            voltage_max=voltage_max,
            ramp_rate_v_per_sec=ramp_rate_v_per_sec,
            max_timestamp_age_sec=max_timestamp_age_sec,
            ct_filter_window_samples=ct_filter_window_samples,
            mqtt_enabled=mqtt_enabled,
            mqtt_host=mqtt_host,
            mqtt_port=mqtt_port,
            mqtt_topic=mqtt_topic,
            mqtt_speed_response_topic=mqtt_speed_response_topic,
            ct_to_speed_factor=ct_to_speed_factor,
        )


def _get_bool(name: str, default: bool) -> bool:
    value = os.getenv(name)
    if value is None or value.strip() == "":
        return default
    return value.strip().lower() in {"1", "true", "yes", "on"}
