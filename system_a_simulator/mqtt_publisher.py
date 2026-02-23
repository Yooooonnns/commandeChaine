"""MQTT publisher utility for publishing cycle time calculations."""
import json
import logging
from datetime import datetime, timezone
from typing import Optional, Any
from paho.mqtt import client as mqtt_client

logger = logging.getLogger(__name__)

class CtPublisher:
    """MQTT publisher for cycle time (CT) values."""
    
    def __init__(self, broker_host: str, broker_port: int, username: str = "", password: str = ""):
        self._broker_host = broker_host
        self._broker_port = broker_port
        self._username = username
        self._password = password
        self._client = mqtt_client.Client()
        self._client.on_connect = self._on_connect
        self._client.on_disconnect = self._on_disconnect
        self._is_connected = False
        
    def connect(self):
        try:
            if self._username and self._password:
                self._client.username_pw_set(self._username, self._password)
            self._client.connect(self._broker_host, self._broker_port, keepalive=60)
            self._client.loop_start()
            logger.info("MQTT publisher connecting to %s:%s", self._broker_host, self._broker_port)
        except Exception as exc:
            logger.error("Failed to connect to MQTT broker: %s", exc)
            raise
    
    def disconnect(self):
        try:
            self._client.loop_stop()
            self._client.disconnect()
            logger.info("MQTT publisher disconnected")
        except Exception as exc:
            logger.error("Error disconnecting from MQTT broker: %s", exc)
    
    def _on_connect(self, client: Any, userdata: Any, flags: Any, reason_code: Any):
        if reason_code == 0:
            self._is_connected = True
            logger.info("MQTT publisher connected to broker")
        else:
            self._is_connected = False
            logger.warning("MQTT publisher connection failed with reason code: %s", reason_code)
    
    def _on_disconnect(self, client: Any, userdata: Any, reason_code: Any):
        self._is_connected = False
        if reason_code != 0:
            logger.warning("MQTT publisher disconnected unexpectedly: %s", reason_code)
    
    def publish_ct(self, line_id: str, ct_seconds: float, chain_state: Optional[dict] = None, jigs: Optional[list] = None) -> bool:
        if not line_id or line_id.strip() == "":
            raise ValueError("line_id must not be empty")
        if ct_seconds <= 0:
            raise ValueError("ct_seconds must be positive")
        if not self._is_connected:
            logger.warning("MQTT publisher not connected, cannot publish")
            return False
        
        topic = f"yazaki/line/{line_id}/ct"
        payload = {
            "line_id": line_id,
            "calculated_ct_seconds": ct_seconds,
            "timestamp": datetime.now(timezone.utc).isoformat(),
            "chain_state": chain_state or {},
            "jigs": jigs or [],
        }
        
        try:
            info = self._client.publish(topic, json.dumps(payload, separators=(",", ":")), qos=1)
            if info.rc == mqtt_client.MQTT_ERR_SUCCESS:
                logger.debug("Published CT to MQTT - line=%s ct_seconds=%.2f", line_id, ct_seconds)
                return True
            else:
                logger.error("MQTT publish failed with return code: %s", info.rc)
                return False
        except Exception as exc:
            logger.error("Exception publishing to MQTT: %s", exc)
            return False
    
    @property
    def is_connected(self) -> bool:
        return self._is_connected
