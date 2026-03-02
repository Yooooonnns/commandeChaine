param(
    [string]$PythonExe = ".venv\Scripts\python.exe",
    [string]$Host = "0.0.0.0",
    [int]$Port = 8000
)

$ErrorActionPreference = "Stop"
$env:PYTHONPATH = "RasberryPi/src"
$env:RASPI_HOST = $Host
$env:RASPI_PORT = "$Port"
$env:RASPI_MQTT_HOST = "localhost"
$env:RASPI_MQTT_PORT = "1883"

& $PythonExe -m raspberry_module.main
