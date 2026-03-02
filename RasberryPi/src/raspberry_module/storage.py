import json
import sqlite3
from datetime import datetime
from pathlib import Path
from typing import Any, Dict

import logging

logger = logging.getLogger(__name__)


class Storage:
    def __init__(self, db_path: Path) -> None:
        self._db_path = db_path
        self._init_db()

    def _connect(self) -> sqlite3.Connection:
        return sqlite3.connect(self._db_path)

    def _init_db(self) -> None:
        with self._connect() as conn:
            conn.execute(
                """
                CREATE TABLE IF NOT EXISTS command_log (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    received_at TEXT NOT NULL,
                    line_id TEXT NOT NULL,
                    speed REAL NOT NULL,
                    mode TEXT NOT NULL,
                    timestamp TEXT NOT NULL,
                    status TEXT NOT NULL,
                    reason TEXT NOT NULL,
                    raw_json TEXT NOT NULL
                )
                """
            )
            conn.execute(
                """
                CREATE TABLE IF NOT EXISTS output_log (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    created_at TEXT NOT NULL,
                    speed_used REAL NOT NULL,
                    voltage REAL NOT NULL,
                    reason TEXT NOT NULL
                )
                """
            )
            conn.execute(
                """
                CREATE TABLE IF NOT EXISTS event_log (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    created_at TEXT NOT NULL,
                    level TEXT NOT NULL,
                    message TEXT NOT NULL
                )
                """
            )

    def log_command(
        self,
        received_at: datetime,
        line_id: str,
        speed: float,
        mode: str,
        timestamp: datetime,
        status: str,
        reason: str,
        raw_json: Dict[str, Any],
    ) -> None:
        with self._connect() as conn:
            conn.execute(
                """
                INSERT INTO command_log (
                    received_at, line_id, speed, mode, timestamp, status, reason, raw_json
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    received_at.isoformat(),
                    line_id,
                    speed,
                    mode,
                    timestamp.isoformat(),
                    status,
                    reason,
                    json.dumps(raw_json, separators=(",", ":")),
                ),
            )

    def log_output(
        self, created_at: datetime, speed_used: float, voltage: float, reason: str
    ) -> None:
        with self._connect() as conn:
            conn.execute(
                """
                INSERT INTO output_log (created_at, speed_used, voltage, reason)
                VALUES (?, ?, ?, ?)
                """,
                (created_at.isoformat(), speed_used, voltage, reason),
            )

    def log_event(self, created_at: datetime, level: str, message: str) -> None:
        with self._connect() as conn:
            conn.execute(
                """
                INSERT INTO event_log (created_at, level, message)
                VALUES (?, ?, ?)
                """,
                (created_at.isoformat(), level, message),
            )

    def export_csv(self, output_dir: Path) -> Dict[str, str]:
        output_dir.mkdir(parents=True, exist_ok=True)
        exports = {}

        exports["command_log"] = self._export_table(
            "command_log", output_dir / "command_log.csv"
        )
        exports["output_log"] = self._export_table(
            "output_log", output_dir / "output_log.csv"
        )
        exports["event_log"] = self._export_table(
            "event_log", output_dir / "event_log.csv"
        )
        return exports

    def _export_table(self, table_name: str, output_path: Path) -> str:
        with self._connect() as conn:
            cur = conn.execute(f"SELECT * FROM {table_name}")
            rows = cur.fetchall()
            headers = [desc[0] for desc in cur.description]

        lines = [",".join(headers)]
        for row in rows:
            lines.append(
                ",".join(
                    "\"" + str(value).replace("\"", "\"\"") + "\"" for value in row
                )
            )

        output_path.write_text("\n".join(lines), encoding="utf-8")
        return str(output_path)
