# System B - Raspberry Pi Control Simulator

Speed controller simulator for the Yazaki Commande Chaine distributed MQTT architecture.

## Overview

System B is responsible for:
- Subscribing to cycle time (CT) messages from System A via MQTT
- Processing CT values through a speed controller simulator
- Applying voltage ramping to simulate motor control
- Sending control results to an API callback URL
- Persisting all events to SQLite database

## Running

python -m raspberry_simulator.app

Service will start on http://localhost:9002

## API Endpoints

GET /api/v1/health - Health check
GET /api/v1/state - Current system state
POST /api/v1/command - Manual speed command
GET /api/v1/export - Export control logs as CSV

## License

YAZAKI

## Version

1.0.0
