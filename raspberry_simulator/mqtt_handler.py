"""MQTT subscription handler for System B."""
import json
import logging
from paho.mqtt import client as mqtt_client

logger = logging.getLogger(__name__)

class MqttSubscriptionHandler:
    def __init__(self, config, on_ct_received):
        self._config = config
        self._on_ct_received = on_ct_received
        self._client = mqtt_client.Client()
        self._client.on_connect = self._on_connect
        self._client.on_disconnect = self._on_disconnect
        self._client.on_message = self._on_message
        self._is_connected = False
    
    def start(self):
        try:
            if self._config.mqtt_username and self._config.mqtt_password:
                self._client.username_pw_set(self._config.mqtt_username, self._config.mqtt_password)
            self._client.connect(self._config.mqtt_host, self._config.mqtt_port, keepalive=60)
            self._client.loop_start()
            logger.info("MQTT handler connecting to %s:%s topic=%s", self._config.mqtt_host, self._config.mqtt_port, self._config.mqtt_topic)
        except Exception as exc:
            logger.error("Failed to connect to MQTT broker: %s", exc)
            raise
    
    def stop(self):
        try:
            self._client.loop_stop()
            self._client.disconnect()
            logger.info("MQTT handler stopped")
        except Exception as exc:
            logger.error("Error stopping MQTT handler: %s", exc)
    
    def _on_connect(self, client, userdata, flags, reason_code):
        if reason_code == 0:
            client.subscribe(self._config.mqtt_topic)
            self._is_connected = True
            logger.info("MQTT handler connected and subscribed to %s", self._config.mqtt_topic)
        else:
            self._is_connected = False
            logger.warning("MQTT connection failed with reason code: %s", reason_code)
    
    def _on_disconnect(self, client, userdata, reason_code):
        self._is_connected = False
        if reason_code != 0:
            logger.warning("MQTT handler disconnected unexpectedly: %s", reason_code)
    
    def _on_message(self, client, userdata, msg):
        try:
            payload = json.loads(msg.payload.decode("utf-8"))
            if "line_id" not in payload:
                raise ValueError("missing line_id")
            if "calculated_ct_seconds" not in payload:
                raise ValueError("missing calculated_ct_seconds")
            line_id = payload.get("line_id")
            ct_seconds = float(payload.get("calculated_ct_seconds"))
            chain_state = payload.get("chain_state", {})
            if ct_seconds <= 0:
                raise ValueError("calculated_ct_seconds must be > 0")
            logger.debug("Received MQTT CT message - line=%s ct_seconds=%.2f", line_id, ct_seconds)
            self._on_ct_received(line_id, ct_seconds, chain_state)
        except json.JSONDecodeError as exc:
            logger.warning("Failed to parse MQTT message JSON: %s", exc)
        except ValueError as exc:
            logger.warning("Invalid MQTT message: %s", exc)
        except Exception as exc:
            logger.error("Unexpected error processing MQTT message: %s", exc)
    
    @property
    def is_connected(self):
        return self._is_connected
