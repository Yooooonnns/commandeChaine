param(
    [string]$PythonExe = ".venv\Scripts\python.exe",
    [string]$Host = "0.0.0.0",
    [int]$Port = 9001
)

$ErrorActionPreference = "Stop"
$env:PYTHONPATH = "RasberryPi/src"
$env:SYSTEM_A_HOST = $Host
$env:SYSTEM_A_PORT = "$Port"
$env:MQTT_BROKER_HOST = "localhost"
$env:MQTT_BROKER_PORT = "1883"

& $PythonExe -m system_a_simulator.app
