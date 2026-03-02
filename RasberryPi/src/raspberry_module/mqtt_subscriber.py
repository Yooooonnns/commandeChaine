import json
import logging
from datetime import datetime, timezone
from typing import Any

from paho.mqtt import client as mqtt_client

from .config import AppConfig
from .control import SpeedController

logger = logging.getLogger(__name__)


class CycleTimeMqttSubscriber:
    def __init__(self, config: AppConfig, controller: SpeedController) -> None:
        self._config = config
        self._controller = controller
        self._client = mqtt_client.Client()
        self._client.on_connect = self._on_connect
        self._client.on_message = self._on_message

    def start(self) -> None:
        if not self._config.mqtt_enabled:
            logger.info("MQTT disabled in configuration.")
            return

        logger.info("Connecting MQTT subscriber to %s:%s topic=%s", self._config.mqtt_host, self._config.mqtt_port, self._config.mqtt_topic)
        self._client.connect(self._config.mqtt_host, self._config.mqtt_port, 60)
        self._client.loop_start()

    def stop(self) -> None:
        try:
            self._client.loop_stop()
            self._client.disconnect()
        except Exception:
            pass

    def _on_connect(self, client: Any, userdata: Any, flags: Any, reason_code: Any) -> None:
        if reason_code == 0:
            client.subscribe(self._config.mqtt_topic)
            logger.info("MQTT connected and subscribed to %s", self._config.mqtt_topic)
        else:
            logger.warning("MQTT connection failed: %s", reason_code)

    def _on_message(self, client: Any, userdata: Any, msg: Any) -> None:
        try:
            payload = json.loads(msg.payload.decode("utf-8"))
            line_id = payload.get("line_id") or "L1"

            if "calculated_ct_seconds" not in payload:
                raise ValueError("missing calculated_ct_seconds")
            if "chain_state" not in payload:
                raise ValueError("missing chain_state")
            if "jigs" not in payload:
                raise ValueError("missing jigs")

            ct_seconds = float(payload.get("calculated_ct_seconds"))
            if ct_seconds <= 0:
                raise ValueError("calculated_ct_seconds must be > 0")

            chain_state = payload.get("chain_state") or {}
            ct_minutes = ct_seconds / 60.0

            result = self._controller.process_cycle_time(
                line_id=line_id,
                cycle_time_minutes=ct_minutes,
                mode="mqtt",
                chain_state=chain_state,
            )
            logger.info(
                "MQTT CT processed line=%s ct_seconds=%s speed=%s voltage=%s status=%s",
                line_id,
                ct_seconds,
                result.speed_used,
                result.voltage,
                result.status,
            )

            # Publish speed response back to the API
            self._publish_speed_response(line_id, result.speed_used, result.voltage, ct_seconds)

        except Exception as exc:
            logger.warning("Failed to process MQTT CT payload: %s; payload=%s", exc, msg.payload)

    def _publish_speed_response(self, line_id: str, speed_rpm: float, voltage: float, ct_seconds: float) -> None:
        """Publish calculated speed back to the API via MQTT."""
        try:
            topic = self._config.mqtt_speed_response_topic.replace("{line_id}", str(line_id))
            response_payload = json.dumps({
                "line_id": line_id,
                "speed_rpm": round(speed_rpm, 2),
                "voltage": round(voltage, 3),
                "ct_seconds": ct_seconds,
                "timestamp": datetime.now(timezone.utc).isoformat(),
            })
            self._client.publish(topic, response_payload)
            logger.info("Published speed response to %s: speed_rpm=%s voltage=%s", topic, speed_rpm, voltage)
        except Exception as exc:
            logger.warning("Failed to publish speed response: %s", exc)
