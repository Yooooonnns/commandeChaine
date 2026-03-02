param(
    [switch]$Build
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'Yazaki.CommandeChaine.slnx'

if ($Build) {
    Write-Host "Building Release..." -ForegroundColor Cyan
    dotnet build $solution -c Release
}

$launcherExe = Join-Path $root 'src\Yazaki.CommandeChaine.Launcher\bin\Release\net9.0-windows\Yazaki.CommandeChaine.Launcher.exe'
if (-not (Test-Path $launcherExe)) {
    throw "Release launcher not found: $launcherExe`nRun: .\tools\Run-Release.ps1 -Build"
}

Start-Process -FilePath $launcherExe -WorkingDirectory (Split-Path $launcherExe -Parent)
