"""Configuration loader for System A Simulator."""
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
class SystemAConfig:
    base_dir: Path
    data_dir: Path
    log_path: Path
    port: int = 9001
    host: str = "0.0.0.0"
    mqtt_enabled: bool = True
    mqtt_host: str = "localhost"
    mqtt_port: int = 1883
    mqtt_username: str = ""
    mqtt_password: str = ""
    debug: bool = False
    
    @classmethod
    def load(cls) -> "SystemAConfig":
        base_dir = Path(__file__).resolve().parents[2]
        data_dir = base_dir / "data"
        data_dir.mkdir(parents=True, exist_ok=True)
        log_path = Path(os.getenv("SYSTEM_A_LOG_PATH", data_dir / "system_a.log"))
        port = _get_int("SYSTEM_A_PORT", 9001)
        host = os.getenv("SYSTEM_A_HOST", "0.0.0.0")
        mqtt_enabled = _get_bool("SYSTEM_A_MQTT_ENABLED", True)
        mqtt_host = os.getenv("MQTT_BROKER_HOST", "localhost")
        mqtt_port = _get_int("MQTT_BROKER_PORT", 1883)
        mqtt_username = os.getenv("MQTT_USERNAME", "")
        mqtt_password = os.getenv("MQTT_PASSWORD", "")
        debug = _get_bool("SYSTEM_A_DEBUG", False)
        
        return cls(
            base_dir=base_dir, data_dir=data_dir, log_path=log_path,
            port=port, host=host, mqtt_enabled=mqtt_enabled,
            mqtt_host=mqtt_host, mqtt_port=mqtt_port,
            mqtt_username=mqtt_username, mqtt_password=mqtt_password, debug=debug,
        )
