"""Configuration loader for System B Simulator."""
from dataclasses import dataclass
from pathlib import Path
import os

def _get_float(name: str, default: float) -> float:
    value = os.getenv(name)
    return default if value is None or value.strip() == "" else float(value)

def _get_int(name: str, default: int) -> int:
    value = os.getenv(name)
    return default if value is None or value.strip() == "" else int(value)

def _get_bool(name: str, default: bool) -> bool:
    value = os.getenv(name)
    return default if value is None or value.strip() == "" else value.strip().lower() in {"1", "true", "yes", "on"}

@dataclass(frozen=True)
class SystemBConfig:
    base_dir: Path
    data_dir: Path
    db_path: Path
    log_path: Path
    port: int = 9002
    host: str = "0.0.0.0"
    mqtt_enabled: bool = True
    mqtt_host: str = "localhost"
    mqtt_port: int = 1883
    mqtt_username: str = ""
    mqtt_password: str = ""
    mqtt_topic: str = "yazaki/line/+/ct"
    speed_min: float = 20.0
    speed_max: float = 80.0
    default_speed: float = 50.0
    voltage_min: float = 0.0
    voltage_max: float = 10.0
    ramp_rate_v_per_sec: float = 1.0
    max_timestamp_age_sec: int = 30
    ct_filter_window_samples: int = 5
    ct_to_speed_factor: float = 1.0
    api_callback_url: str = "http://localhost:5000/api/simulation-results"
    api_callback_enabled: bool = True
    api_callback_timeout_sec: int = 5
    api_callback_max_retries: int = 3
    debug: bool = False
    
    @classmethod
    def load(cls) -> "SystemBConfig":
        base_dir = Path(__file__).resolve().parents[2]
        data_dir = base_dir / "data"
        data_dir.mkdir(parents=True, exist_ok=True)
        db_path = Path(os.getenv("SYSTEM_B_DB_PATH", data_dir / "system_b.db"))
        log_path = Path(os.getenv("SYSTEM_B_LOG_PATH", data_dir / "system_b.log"))
        port = _get_int("SYSTEM_B_PORT", 9002)
        host = os.getenv("SYSTEM_B_HOST", "0.0.0.0")
        mqtt_enabled = _get_bool("SYSTEM_B_MQTT_ENABLED", True)
        mqtt_host = os.getenv("MQTT_BROKER_HOST", "localhost")
        mqtt_port = _get_int("MQTT_BROKER_PORT", 1883)
        mqtt_username = os.getenv("MQTT_USERNAME", "")
        mqtt_password = os.getenv("MQTT_PASSWORD", "")
        mqtt_topic = os.getenv("SYSTEM_B_MQTT_TOPIC", "yazaki/line/+/ct")
        speed_min = _get_float("SYSTEM_B_SPEED_MIN", 20.0)
        speed_max = _get_float("SYSTEM_B_SPEED_MAX", 80.0)
        default_speed = _get_float("SYSTEM_B_DEFAULT_SPEED", 50.0)
        voltage_min = _get_float("SYSTEM_B_VOLTAGE_MIN", 0.0)
        voltage_max = _get_float("SYSTEM_B_VOLTAGE_MAX", 10.0)
        ramp_rate_v_per_sec = _get_float("SYSTEM_B_RAMP_RATE_V_PER_SEC", 1.0)
        max_timestamp_age_sec = _get_int("SYSTEM_B_MAX_TIMESTAMP_AGE_SEC", 30)
        ct_filter_window_samples = max(1, _get_int("SYSTEM_B_CT_FILTER_WINDOW_SAMPLES", 5))
        ct_to_speed_factor = _get_float("SYSTEM_B_CT_TO_SPEED_FACTOR", 1.0)
        api_callback_url = os.getenv("API_CALLBACK_URL", "http://localhost:5000/api/simulation-results")
        api_callback_enabled = _get_bool("API_CALLBACK_ENABLED", True)
        api_callback_timeout_sec = _get_int("API_CALLBACK_TIMEOUT_SEC", 5)
        api_callback_max_retries = _get_int("API_CALLBACK_MAX_RETRIES", 3)
        debug = _get_bool("SYSTEM_B_DEBUG", False)
        
        return cls(
            base_dir=base_dir, data_dir=data_dir, db_path=db_path, log_path=log_path,
            port=port, host=host, mqtt_enabled=mqtt_enabled,
            mqtt_host=mqtt_host, mqtt_port=mqtt_port, mqtt_username=mqtt_username,
            mqtt_password=mqtt_password, mqtt_topic=mqtt_topic,
            speed_min=speed_min, speed_max=speed_max, default_speed=default_speed,
            voltage_min=voltage_min, voltage_max=voltage_max, ramp_rate_v_per_sec=ramp_rate_v_per_sec,
            max_timestamp_age_sec=max_timestamp_age_sec, ct_filter_window_samples=ct_filter_window_samples,
            ct_to_speed_factor=ct_to_speed_factor, api_callback_url=api_callback_url,
            api_callback_enabled=api_callback_enabled, api_callback_timeout_sec=api_callback_timeout_sec,
            api_callback_max_retries=api_callback_max_retries, debug=debug,
        )
