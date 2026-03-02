param(
    [Parameter(Mandatory = $true)]
    [string]$Host,

    [string]$User = "pi",
    [int]$Port = 22,
    [string]$RemoteDir = "/home/pi/raspberry-commande",
    [string]$KeyPath = "",
    [string]$PythonBin = "python3"
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command ssh -ErrorAction SilentlyContinue)) {
    throw "OpenSSH client not found (ssh)."
}

$args = @()
if ($KeyPath) { $args += @("-i", $KeyPath) }
$args += @("-p", "$Port", "$User@$Host")
$args += "cd '$RemoteDir/src' && nohup $PythonBin -m raspberry_module.main > '$RemoteDir/data/raspberry.out.log' 2>&1 & echo started"

& ssh @args
if ($LASTEXITCODE -ne 0) {
    throw "Failed to start raspberry module on remote host."
}

Write-Host "Raspberry module start command sent."
