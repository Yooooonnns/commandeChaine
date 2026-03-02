"""SQLite database persistence for System B."""
import json
import sqlite3
from datetime import datetime
from pathlib import Path
import logging

logger = logging.getLogger(__name__)

class Database:
    def __init__(self, db_path: Path):
        self._db_path = db_path
        self._init_db()
    
    def _connect(self) -> sqlite3.Connection:
        return sqlite3.connect(self._db_path)
    
    def _init_db(self):
        with self._connect() as conn:
            conn.execute("""CREATE TABLE IF NOT EXISTS control_logs (
                id INTEGER PRIMARY KEY AUTOINCREMENT, line_id TEXT NOT NULL, ct_seconds REAL NOT NULL,
                filtered_ct_seconds REAL NOT NULL, voltage REAL NOT NULL, speed REAL NOT NULL,
                timestamp TEXT NOT NULL, created_at TEXT NOT NULL)""")
            conn.execute("""CREATE TABLE IF NOT EXISTS mqtt_messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT, line_id TEXT NOT NULL, topic TEXT NOT NULL,
                payload TEXT NOT NULL, received_at TEXT NOT NULL)""")
            conn.execute("""CREATE TABLE IF NOT EXISTS api_callbacks (
                id INTEGER PRIMARY KEY AUTOINCREMENT, line_id TEXT NOT NULL, api_url TEXT NOT NULL,
                status TEXT NOT NULL, http_status INTEGER, error_message TEXT, timestamp TEXT NOT NULL)""")
            conn.commit()
            logger.info("Database initialized: %s", self._db_path)
    
    def save_control_log(self, line_id, ct_seconds, filtered_ct_seconds, voltage, speed, timestamp):
        with self._connect() as conn:
            cursor = conn.execute(
                "INSERT INTO control_logs (line_id, ct_seconds, filtered_ct_seconds, voltage, speed, timestamp, created_at) VALUES (?, ?, ?, ?, ?, ?, ?)",
                (line_id, ct_seconds, filtered_ct_seconds, voltage, speed, timestamp.isoformat(), datetime.now().isoformat()),
            )
            conn.commit()
            return cursor.lastrowid
    
    def save_mqtt_message(self, line_id, topic, payload, received_at):
        with self._connect() as conn:
            cursor = conn.execute(
                "INSERT INTO mqtt_messages (line_id, topic, payload, received_at) VALUES (?, ?, ?, ?)",
                (line_id, topic, json.dumps(payload, separators=(",", ":")), received_at.isoformat()),
            )
            conn.commit()
            return cursor.lastrowid
    
    def save_api_callback_log(self, line_id, api_url, status, http_status=None, error_message=None):
        with self._connect() as conn:
            cursor = conn.execute(
                "INSERT INTO api_callbacks (line_id, api_url, status, http_status, error_message, timestamp) VALUES (?, ?, ?, ?, ?, ?)",
                (line_id, api_url, status, http_status, error_message, datetime.now().isoformat()),
            )
            conn.commit()
            return cursor.lastrowid
    
    def get_control_logs(self, line_id=None, limit=100):
        with self._connect() as conn:
            if line_id:
                cursor = conn.execute(
                    "SELECT id, line_id, ct_seconds, filtered_ct_seconds, voltage, speed, timestamp FROM control_logs WHERE line_id = ? ORDER BY timestamp DESC LIMIT ?",
                    (line_id, limit),
                )
            else:
                cursor = conn.execute(
                    "SELECT id, line_id, ct_seconds, filtered_ct_seconds, voltage, speed, timestamp FROM control_logs ORDER BY timestamp DESC LIMIT ?",
                    (limit,),
                )
            return [{"id": row[0], "line_id": row[1], "ct_seconds": row[2], "filtered_ct_seconds": row[3], "voltage": row[4], "speed": row[5], "timestamp": row[6]} for row in cursor.fetchall()]
    
    def export_csv(self, line_id=None):
        logs = self.get_control_logs(line_id=line_id, limit=1000)
        if not logs:
            return "id,line_id,ct_seconds,filtered_ct_seconds,voltage,speed,timestamp\n"
        csv_lines = ["id,line_id,ct_seconds,filtered_ct_seconds,voltage,speed,timestamp"]
        for log in logs:
            csv_lines.append(f'{log["id"]},{log["line_id"]},{log["ct_seconds"]:.2f},{log["filtered_ct_seconds"]:.2f},{log["voltage"]:.2f},{log["speed"]:.2f},{log["timestamp"]}')
        return "\n".join(csv_lines)
