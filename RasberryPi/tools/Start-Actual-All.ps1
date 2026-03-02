param(
    [string]$PythonExe = ".venv\Scripts\python.exe",
    [switch]$InstallDeps
)

$ErrorActionPreference = "Stop"

if ($InstallDeps) {
    & $PythonExe -m pip install -r "RasberryPi/requirements.txt"
    & $PythonExe -m pip install -r "RasberryPi/src/system_a_simulator/requirements.txt"
}

$workspace = (Get-Location).Path
$cmdSystemB = "Set-Location -Path '$workspace'; ./RasberryPi/tools/Start-Actual-SystemB.ps1 -PythonExe '$PythonExe'"
$cmdSystemA = "Set-Location -Path '$workspace'; ./RasberryPi/tools/Start-Actual-SystemA.ps1 -PythonExe '$PythonExe'"

Start-Process powershell -ArgumentList "-NoExit", "-Command", $cmdSystemB | Out-Null
Start-Sleep -Seconds 1
Start-Process powershell -ArgumentList "-NoExit", "-Command", $cmdSystemA | Out-Null

Write-Host "Started actual SystemB (raspberry_module) and SystemA (system_a_simulator)."
