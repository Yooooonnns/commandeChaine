param(
    [Parameter(Mandatory = $true)]
    [string]$Host,

    [string]$User = "pi",
    [int]$Port = 22,
    [string]$RemoteDir = "/home/pi/raspberry-commande",
    [string]$KeyPath = "",
    [switch]$InstallDeps,
    [switch]$StartService,
    [string]$PythonBin = "python3"
)

$ErrorActionPreference = "Stop"

function Invoke-Ssh {
    param([string]$Command)

    $args = @()
    if ($KeyPath) { $args += @("-i", $KeyPath) }
    $args += @("-p", "$Port", "$User@$Host", $Command)

    & ssh @args
    if ($LASTEXITCODE -ne 0) {
        throw "ssh command failed: $Command"
    }
}

function Invoke-Scp {
    param(
        [string]$Source,
        [string]$Target,
        [switch]$Recursive
    )

    $args = @()
    if ($KeyPath) { $args += @("-i", $KeyPath) }
    $args += @("-P", "$Port")
    if ($Recursive) { $args += "-r" }
    $args += @($Source, $Target)

    & scp @args
    if ($LASTEXITCODE -ne 0) {
        throw "scp failed: $Source -> $Target"
    }
}

if (-not (Get-Command ssh -ErrorAction SilentlyContinue)) {
    throw "OpenSSH client not found (ssh). Install Windows OpenSSH Client first."
}
if (-not (Get-Command scp -ErrorAction SilentlyContinue)) {
    throw "OpenSSH scp not found. Install Windows OpenSSH Client first."
}

$projectRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Preparing remote directory $RemoteDir on $User@$Host..."
Invoke-Ssh "mkdir -p '$RemoteDir' '$RemoteDir/src' '$RemoteDir/data'"

Write-Host "Copying project files..."
Invoke-Scp -Source (Join-Path $projectRoot "requirements.txt") -Target "${User}@${Host}:$RemoteDir/requirements.txt"
Invoke-Scp -Source (Join-Path $projectRoot "pyproject.toml") -Target "${User}@${Host}:$RemoteDir/pyproject.toml"
Invoke-Scp -Source (Join-Path $projectRoot ".env.example") -Target "${User}@${Host}:$RemoteDir/.env.example"
Invoke-Scp -Source (Join-Path $projectRoot "src") -Target "${User}@${Host}:$RemoteDir" -Recursive

if ($InstallDeps) {
    Write-Host "Installing Python dependencies on Raspberry..."
    $installCmd = "cd '$RemoteDir' && $PythonBin -m pip install --upgrade pip && $PythonBin -m pip install -r requirements.txt"
    Invoke-Ssh $installCmd
}

if ($StartService) {
    Write-Host "Starting raspberry_module.main on Raspberry..."
    $startCmd = "cd '$RemoteDir/src' && nohup $PythonBin -m raspberry_module.main > '$RemoteDir/data/raspberry.out.log' 2>&1 & echo started"
    Invoke-Ssh $startCmd
}

Write-Host "Deployment finished."
Write-Host "Next step: test health endpoint from your PC:"
Write-Host "  http://<raspberry-ip>:8000/api/v1/health"
