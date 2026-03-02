# Raspberry Commande - Auto Speed Control

Python service for receiving digital speed commands, validating them, converting to a 0-10 V output (simulated), and logging events for reporting. Includes a software-only simulator to test the full pipeline without hardware.

It also supports MQTT cycle-time ingestion (`CT`) and converts `CT -> speed` on Raspberry side.

## Setup (Windows)
1. Create and activate a virtual env:
   - `python -m venv .venv`
   - `\.venv\Scripts\activate`
2. Install dependencies:
   - `pip install -r requirements.txt`

## Run the API service
- `python -m raspberry_module.main`

Default host/port: `0.0.0.0:8000`

## Send a command (example)
```bash
curl -X POST http://localhost:8000/api/v1/command \
  -H "Content-Type: application/json" \
  -d "{\"line_id\":\"L1\",\"speed\":55.0,\"mode\":\"auto\",\"timestamp\":\"2026-02-11 10:00:00\"}"
```

## Run the simulator
Direct mode (no HTTP):
- `python -m raspberry_module.simulator --duration 30 --interval 1.0`

HTTP mode (requires API running):
- `python -m raspberry_module.simulator --mode http --api-url http://localhost:8000/api/v1/command`

## Configuration
Copy `.env.example` to `.env` and adjust values. Environment variables are optional and override defaults.

MQTT-related variables:
- `RASPI_MQTT_ENABLED` (default `true`)
- `RASPI_MQTT_HOST` (default `localhost`)
- `RASPI_MQTT_PORT` (default `1883`)
- `RASPI_MQTT_TOPIC` (default `yazaki/line/+/ct`)
- `RASPI_CT_TO_SPEED_FACTOR` (default `1.0`)

## Deploy to a real Raspberry Pi over SSH

From Windows PowerShell (repo root):

1. Copy files to Raspberry:
   - `./RasberryPi/tools/Deploy-Raspberry.ps1 -Host <RASPBERRY_IP> -User pi -InstallDeps`

2. Start the service remotely:
   - `./RasberryPi/tools/Start-RaspberryRemote.ps1 -Host <RASPBERRY_IP> -User pi`

3. Verify from your PC:
   - `http://<RASPBERRY_IP>:8000/api/v1/health`

Optional parameters for both scripts:
- `-Port 22`
- `-KeyPath <path-to-private-key>`
- `-RemoteDir /home/pi/raspberry-commande`

## Wire desktop app + API to remote Raspberry

Edit `Yazaki.CommandeChaine/yazaki.settings.json`:
- `RaspberryPiApiUrl`: `http://<RASPBERRY_IP>:8000`
- `RaspberryPiHealthPath`: `/api/v1/health`
- `RaspberryPiAutoStart`: `false`

With this setup, launcher will not try to start local Python and API will send commands to your remote Raspberry.
