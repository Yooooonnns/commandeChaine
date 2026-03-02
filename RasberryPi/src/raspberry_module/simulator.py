import argparse
import json
import random
import time
from datetime import datetime, timezone
from urllib import request

from .config import AppConfig
from .control import SpeedController
from .models import CommandIn
from .storage import Storage
from .logging_utils import setup_logging


def _generate_speed(config: AppConfig) -> float:
    span = config.speed_max - config.speed_min
    if span <= 0:
        return config.default_speed

    buckets = [
        (0.75, 1.0),  # simple
        (0.45, 0.75),  # medium
        (0.2, 0.45),  # complex
    ]
    low_ratio, high_ratio = random.choice(buckets)
    return config.speed_min + random.uniform(low_ratio, high_ratio) * span


def _build_payload(speed: float) -> dict:
    return {
        "line_id": "L1",
        "speed": round(speed, 2),
        "mode": "auto",
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S"),
    }


def _send_http(url: str, payload: dict) -> None:
    data = json.dumps(payload).encode("utf-8")
    req = request.Request(url, data=data, method="POST")
    req.add_header("Content-Type", "application/json")
    with request.urlopen(req, timeout=5) as resp:
        resp.read()


def _run_direct(duration: int, interval: float) -> None:
    config = AppConfig.load()
    setup_logging(config.log_path)
    storage = Storage(config.db_path)
    controller = SpeedController(config, storage)

    end_time = time.time() + duration
    while time.time() < end_time:
        speed = _generate_speed(config)
        payload = _build_payload(speed)
        command = CommandIn(**payload)
        controller.process_command(command)
        time.sleep(interval)


def _run_http(duration: int, interval: float, url: str) -> None:
    end_time = time.time() + duration
    while time.time() < end_time:
        payload = _build_payload(random.uniform(20.0, 80.0))
        _send_http(url, payload)
        time.sleep(interval)


def main() -> None:
    parser = argparse.ArgumentParser(description="Raspberry module simulator")
    parser.add_argument("--duration", type=int, default=30)
    parser.add_argument("--interval", type=float, default=1.0)
    parser.add_argument("--mode", choices=["direct", "http"], default="direct")
    parser.add_argument(
        "--api-url", default="http://localhost:8000/api/v1/command"
    )
    args = parser.parse_args()

    if args.mode == "direct":
        _run_direct(args.duration, args.interval)
    else:
        _run_http(args.duration, args.interval, args.api_url)


if __name__ == "__main__":
    main()
