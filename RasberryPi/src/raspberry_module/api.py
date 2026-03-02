from datetime import datetime, timezone
from fastapi import FastAPI, HTTPException

from .config import AppConfig
from .control import SpeedController
from .models import CommandIn, CommandOut
from .storage import Storage


def create_app() -> FastAPI:
    config = AppConfig.load()
    storage = Storage(config.db_path)
    controller = SpeedController(config, storage)

    app = FastAPI(title="Raspberry Commande", version="1.0.0")
    app.state.config = config
    app.state.storage = storage
    app.state.controller = controller

    @app.get("/api/v1/health")
    def health() -> dict:
        return {"status": "ok", "timestamp": datetime.now(timezone.utc).isoformat()}

    @app.get("/api/v1/state")
    def state() -> dict:
        chain_state = controller.last_chain_state
        return {
            "last_valid_speed": controller.last_valid_speed,
            "last_voltage": controller.last_voltage,
            "last_filtered_cycle_time": controller.last_filtered_cycle_time,
            "chain_state": {
                "is_running": chain_state.is_running if chain_state else None,
                "encoder_delta": chain_state.encoder_delta if chain_state else None,
                "updated_at": chain_state.updated_at.isoformat() if chain_state else None,
            },
        }

    @app.post("/api/v1/command", response_model=CommandOut)
    def command(payload: CommandIn) -> CommandOut:
        try:
            result = controller.process_command(payload)
        except Exception as exc:
            raise HTTPException(status_code=500, detail=str(exc)) from exc

        return CommandOut(
            status=result.status,
            speed_used=result.speed_used,
            voltage=result.voltage,
            reason=result.reason,
            applied_at=result.applied_at,
        )

    @app.post("/api/v1/export")
    def export() -> dict:
        exports = storage.export_csv(config.data_dir / "exports")
        return {"exports": exports}

    return app
