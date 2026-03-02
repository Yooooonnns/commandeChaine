# System A - CT Calculator Simulator

CT (Cycle Time) Calculator for the Yazaki Commande Chaine distributed MQTT architecture.

## Overview

System A is responsible for:
- Calculating standardized cycle times using the Heijunka formula
- Publishing CT values to MQTT for consumption by production systems
- Providing REST API for manual CT input
- Validating production metrics and worker counts

## Features

- **Heijunka Formula**: Calculate CT from batch production times accounting for worker count and productivity
- **MQTT Publishing**: Publish CT values to distributed subscribers
- **REST API**: HTTP endpoints for batch input and manual CT submission
- **Health Monitoring**: Built-in health check endpoint with MQTT status
- **Configuration**: Environment-based configuration for development and production

## Installation

### Prerequisites
- Python 3.13+
- pip or conda

### Setup

```bash
# Copy environment configuration
cp system_a_simulator/.env.example system_a_simulator/.env

# Edit .env to match your environment
# Then install dependencies
pip install -r system_a_simulator/requirements.txt
```

## Running

### Development Mode
```bash
python -m uvicorn system_a_simulator.app:create_app --reload --host 0.0.0.0 --port 9001
```

### Production Mode
```bash
uvicorn system_a_simulator.app:create_app --host 0.0.0.0 --port 9001 --workers 4
```

## API Endpoints

### Health Check
```
GET /api/v1/health
```

### Calculate CT from Batch Input
```
POST /api/v1/batch-input
{
  "line_id": "Chaine-01",
  "production_times": [2.5, 3.0, 2.8],
  "worker_count": 3,
  "productivity_factor": 1.0
}
```

### Publish Manual CT Value
```
POST /api/v1/manual-ct
{
  "line_id": "Chaine-01",
  "calculated_ct_seconds": 45.0
}
```

## Heijunka Formula

```
CT (minutes) = Mean(production_times) / worker_count / productivity_factor
```

## MQTT Topic Structure

Published to: `yazaki/line/{line_id}/ct`

Payload format:
```json
{
  "line_id": "Chaine-01",
  "calculated_ct_seconds": 50.0,
  "timestamp": "2024-02-23T10:30:00Z",
  "chain_state": {"is_running": true, "encoder_delta": 1.5},
  "jigs": ["JIG-001"]
}
```

## Testing

```bash
# Test batch CT calculation
curl -X POST http://localhost:9001/api/v1/batch-input \
  -H "Content-Type: application/json" \
  -d '{"line_id": "Chaine-01", "production_times": [2.5, 3.0, 2.8], "worker_count": 3}'

# Test manual CT publishing
curl -X POST http://localhost:9001/api/v1/manual-ct \
  -H "Content-Type: application/json" \
  -d '{"line_id": "Chaine-01", "calculated_ct_seconds": 45.0}'

# Test health status
curl http://localhost:9001/api/v1/health
```

## Configuration Reference

| Variable | Default | Description |
|----------|---------|-------------|
| MQTT_BROKER_HOST | localhost | MQTT broker hostname |
| MQTT_BROKER_PORT | 1883 | MQTT broker port |
| SYSTEM_A_PORT | 9001 | Service port |
| SYSTEM_A_MQTT_ENABLED | true | Enable/disable MQTT publishing |

## Version

1.0.0
