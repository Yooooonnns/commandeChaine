import os
import uvicorn

from .api import create_app
from .config import AppConfig
from .logging_utils import setup_logging
from .mqtt_subscriber import CycleTimeMqttSubscriber


def main() -> None:
    config = AppConfig.load()
    setup_logging(config.log_path)

    host = os.getenv("RASPI_HOST", "0.0.0.0")
    port = int(os.getenv("RASPI_PORT", "8000"))

    app = create_app()

    mqtt_subscriber = CycleTimeMqttSubscriber(config, app.state.controller)
    mqtt_subscriber.start()

    uvicorn.run(app, host=host, port=port, log_level="info")


if __name__ == "__main__":
    main()
