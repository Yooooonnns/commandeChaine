"""System B Simulator - FastAPI application for control simulation."""
import logging
from datetime import datetime, timezone
from contextlib import asynccontextmanager
from fastapi import FastAPI, HTTPException
from fastapi.responses import StreamingResponse

from .config import SystemBConfig
from .database import Database
from .control_simulator import SpeedControllerSimulator
from .mqtt_handler import MqttSubscriptionHandler
from .api_callback import send_results_to_api_sync_wrapper
from .models import ManualCommandRequest, ControlResultResponse, StateResponse, HealthResponse

logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(name)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

def create_app() -> FastAPI:
    config = SystemBConfig.load()
    database = Database(config.db_path)
    controller = SpeedControllerSimulator(config, database)
    mqtt_handler = None
    
    def on_ct_received(line_id, ct_seconds, chain_state):
        try:
            ct_minutes = ct_seconds / 60.0
            result = controller.process_cycle_time(line_id=line_id, cycle_time_minutes=ct_minutes, chain_state=chain_state)
            if config.api_callback_enabled:
                try:
                    send_results_to_api_sync_wrapper(
                        api_url=config.api_callback_url, line_id=line_id, voltage=result["voltage"],
                        speed=result["speed_used"], filtered_ct_seconds=result["filtered_ct_seconds"],
                        timestamp=result["applied_at"], timeout_sec=config.api_callback_timeout_sec,
                        max_retries=config.api_callback_max_retries,
                    )
                except Exception as exc:
                    logger.error("API callback error: %s", exc)
        except Exception as exc:
            logger.error("Error processing MQTT CT message: %s", exc)
    
    @asynccontextmanager
    async def lifespan(app: FastAPI):
        nonlocal mqtt_handler
        if config.mqtt_enabled:
            mqtt_handler = MqttSubscriptionHandler(config, on_ct_received)
            try:
                mqtt_handler.start()
                logger.info("MQTT handler initialized on startup")
            except Exception as exc:
                logger.error("Failed to initialize MQTT handler: %s", exc)
        yield
        if mqtt_handler:
            try:
                mqtt_handler.stop()
            except Exception as exc:
                logger.error("Error stopping MQTT handler: %s", exc)
    
    app = FastAPI(title="System B - Raspberry Pi Control Simulator", version="1.0.0", description="Yazaki Commande Chaine - Control Simulator", lifespan=lifespan)
    app.state.config = config
    app.state.database = database
    app.state.controller = controller
    
    @app.get("/api/v1/health", response_model=HealthResponse)
    async def health() -> HealthResponse:
        mqtt_connected = mqtt_handler.is_connected if mqtt_handler else False
        return HealthResponse(status="ok", mqtt_connected=mqtt_connected, api_enabled=config.api_callback_enabled, timestamp=datetime.now(timezone.utc))
    
    @app.get("/api/v1/state", response_model=StateResponse)
    async def state() -> StateResponse:
        return StateResponse(last_valid_speed=controller.last_valid_speed, last_voltage=controller.last_voltage, last_filtered_cycle_time=controller.last_filtered_cycle_time, chain_state={}, timestamp=datetime.now(timezone.utc))
    
    @app.post("/api/v1/command", response_model=ControlResultResponse)
    async def command(payload: ManualCommandRequest) -> ControlResultResponse:
        try:
            ct_minutes = 60.0 if payload.speed <= 0 else config.ct_to_speed_factor / payload.speed
            result = controller.process_cycle_time(line_id=payload.line_id, cycle_time_minutes=ct_minutes)
            return ControlResultResponse(status=result["status"], line_id=payload.line_id, speed_used=result["speed_used"], voltage=result["voltage"], filtered_ct_seconds=result["filtered_ct_seconds"], timestamp=result["applied_at"])
        except ValueError as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc
        except Exception as exc:
            raise HTTPException(status_code=500, detail=str(exc)) from exc
    
    @app.get("/api/v1/export")
    async def export(line_id: str = None) -> StreamingResponse:
        try:
            csv_content = database.export_csv(line_id=line_id)
            filename = f"control_logs.csv" if not line_id else f"control_logs_{line_id}.csv"
            return StreamingResponse(iter([csv_content]), media_type="text/csv", headers={"Content-Disposition": f"attachment; filename={filename}"})
        except Exception as exc:
            raise HTTPException(status_code=500, detail=str(exc)) from exc
    
    return app

if __name__ == "__main__":
    import uvicorn
    config = SystemBConfig.load()
    app = create_app()
    uvicorn.run(app, host=config.host, port=config.port, log_level="info" if not config.debug else "debug")
