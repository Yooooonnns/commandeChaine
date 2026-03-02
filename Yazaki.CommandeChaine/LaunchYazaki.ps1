param(
    [string]$ApiUrl = "http://localhost:5016",
    [string]$RaspberryUrl = "http://localhost:8000",
    [ValidateSet('Release','Debug')]
    [string]$Configuration = 'Release',
    [switch]$Pull
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

if ($Pull) {
    if (Get-Command git -ErrorAction SilentlyContinue) {
        if (Test-Path (Join-Path $root ".git")) {
            Write-Host "Pulling latest updates (git pull)"
            try { git pull } catch { Write-Warning "git pull failed: $($_.Exception.Message)" }
        }
    }
}

Write-Host "Restoring & building latest code"
dotnet restore | Out-Host
dotnet build -c $Configuration | Out-Host

# Start Raspberry Pi API if Python is available
$pythonAvailable = $false
try {
    $pythonVersion = python --version 2>&1
    if ($LASTEXITCODE -eq 0) {
        $pythonAvailable = $true
        Write-Host "Starting Raspberry Pi API at $RaspberryUrl"
        
        $piArgs = @(
            "-m", "raspberry_module",
            "--host", "localhost",
            "--port", "8000"
        )
        
        $piProcess = Start-Process -FilePath "python" -ArgumentList $piArgs -WorkingDirectory "..\RasberryPi\src" -PassThru -WindowStyle Hidden
        
        # Wait for Raspberry Pi health endpoint
        $piHealthUrl = ($RaspberryUrl.TrimEnd('/') + "/api/v1/health")
        $deadline = (Get-Date).AddSeconds(30)
        
        Write-Host "Waiting for Raspberry Pi API to be ready..."
        while ((Get-Date) -lt $deadline) {
            try {
                $r = Invoke-WebRequest -Uri $piHealthUrl -UseBasicParsing -TimeoutSec 2
                if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 300) {
                    Write-Host "Raspberry Pi API is ready!"
                    break
                }
            } catch {
                Start-Sleep -Milliseconds 500
            }
        }
    }
} catch {
    Write-Warning "Python not found, Raspberry Pi API will not start. Install Python 3.10+ if you need Raspberry Pi support."
}

Write-Host "Starting Yazaki.CommandeChaine API at $ApiUrl"

$apiArgs = @(
    "run",
    "--project", "src\Yazaki.CommandeChaine.Api\Yazaki.CommandeChaine.Api.csproj",
    "-c", $Configuration,
    "--urls", $ApiUrl,
    "--no-build"
)

$apiProcess = Start-Process -FilePath "dotnet" -ArgumentList $apiArgs -PassThru -WindowStyle Hidden

# Wait for health endpoint
$healthUrl = ($ApiUrl.TrimEnd('/') + "/health")
$deadline = (Get-Date).AddSeconds(30)

while ((Get-Date) -lt $deadline) {
    try {
        $r = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 2
        if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 300) {
            break
        }
    } catch {
        Start-Sleep -Milliseconds 500
    }
}

Write-Host "Starting Desktop (API base = $ApiUrl)"
$env:YAZAKI_API_BASE_URL = $ApiUrl

dotnet run --project "src\Yazaki.CommandeChaine.Desktop\Yazaki.CommandeChaine.Desktop.csproj" -c $Configuration --no-build

# If the desktop exits, stop API and Raspberry Pi
try { Stop-Process -Id $apiProcess.Id -Force } catch { }
if ($pythonAvailable -and $null -ne $piProcess) {
    try { Stop-Process -Id $piProcess.Id -Force } catch { }
}

Write-Host "Services stopped."
