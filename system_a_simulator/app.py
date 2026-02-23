"""System A Simulator - FastAPI application for CT calculation and publishing."""
import logging
from datetime import datetime, timezone
from fastapi import FastAPI, HTTPException
from contextlib import asynccontextmanager

from .config import SystemAConfig
from .heijunka import calculate_ct, ct_to_seconds
from .mqtt_publisher import CtPublisher
from .models import BatchInputRequest, ManualCtRequest, CtPublishResponse, HealthResponse

logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(name)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

def create_app() -> FastAPI:
    config = SystemAConfig.load()
    publisher = None
    if config.mqtt_enabled:
        publisher = CtPublisher(
            broker_host=config.mqtt_host, broker_port=config.mqtt_port,
            username=config.mqtt_username, password=config.mqtt_password,
        )
    
    @asynccontextmanager
    async def lifespan(app: FastAPI):
        if publisher:
            try:
                publisher.connect()
                logger.info("MQTT publisher initialized on startup")
            except Exception as exc:
                logger.error("Failed to initialize MQTT publisher: %s", exc)
        yield
        if publisher:
            try:
                publisher.disconnect()
                logger.info("MQTT publisher closed on shutdown")
            except Exception as exc:
                logger.error("Error closing MQTT publisher: %s", exc)
    
    app = FastAPI(
        title="System A - CT Calculator Simulator",
        version="1.0.0",
        description="Yazaki Commande Chaine - Cycle Time Calculator",
        lifespan=lifespan,
    )
    
    app.state.config = config
    app.state.publisher = publisher
    
    @app.get("/api/v1/health", response_model=HealthResponse)
    async def health() -> HealthResponse:
        mqtt_connected = publisher.is_connected if publisher else False
        return HealthResponse(
            status="ok" if mqtt_connected or not config.mqtt_enabled else "degraded",
            mqtt_connected=mqtt_connected,
            timestamp=datetime.now(timezone.utc),
        )
    
    @app.post("/api/v1/batch-input", response_model=CtPublishResponse)
    async def batch_input(payload: BatchInputRequest) -> CtPublishResponse:
        try:
            ct_minutes = calculate_ct(
                production_times=payload.production_times,
                worker_count=payload.worker_count,
                productivity_factor=payload.productivity_factor,
            )
            ct_seconds = ct_to_seconds(ct_minutes)
            logger.info("Calculated CT - line=%s ct_seconds=%.2f", payload.line_id, ct_seconds)
            
            if publisher:
                success = publisher.publish_ct(line_id=payload.line_id, ct_seconds=ct_seconds)
                if not success:
                    raise HTTPException(status_code=503, detail="Failed to publish to MQTT broker")
            
            return CtPublishResponse(
                status="published", line_id=payload.line_id, calculated_ct_seconds=ct_seconds,
                calculated_ct_minutes=ct_minutes, timestamp=datetime.now(timezone.utc),
                reason="CT calculated and published successfully",
            )
        except ValueError as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc
        except Exception as exc:
            raise HTTPException(status_code=500, detail=str(exc)) from exc
    
    @app.post("/api/v1/manual-ct", response_model=CtPublishResponse)
    async def manual_ct(payload: ManualCtRequest) -> CtPublishResponse:
        try:
            if not publisher:
                raise HTTPException(status_code=503, detail="MQTT is disabled")
            success = publisher.publish_ct(line_id=payload.line_id, ct_seconds=payload.calculated_ct_seconds)
            if not success:
                raise HTTPException(status_code=503, detail="Failed to publish to MQTT broker")
            logger.info("Published manual CT - line=%s ct_seconds=%.2f", payload.line_id, payload.calculated_ct_seconds)
            return CtPublishResponse(
                status="published", line_id=payload.line_id, calculated_ct_seconds=payload.calculated_ct_seconds,
                calculated_ct_minutes=payload.calculated_ct_seconds / 60.0, timestamp=datetime.now(timezone.utc),
                reason="Manual CT published successfully",
            )
        except ValueError as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc
        except Exception as exc:
            raise HTTPException(status_code=500, detail=str(exc)) from exc
    
    return app

if __name__ == "__main__":
    import uvicorn
    config = SystemAConfig.load()
    app = create_app()
    uvicorn.run(app, host=config.host, port=config.port, log_level="info" if not config.debug else "debug")
