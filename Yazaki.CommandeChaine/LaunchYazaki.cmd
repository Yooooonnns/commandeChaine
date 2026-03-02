@echo off
setlocal
cd /d "%~dp0"

REM Double-click launcher for Yazaki.CommandeChaine
REM Builds latest code, starts API locally, then starts Desktop.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0LaunchYazaki.ps1" -ApiUrl "http://localhost:5016" -Configuration Release

