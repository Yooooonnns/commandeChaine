param(
    [string]$ShortcutPath
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$launcherExe = Join-Path $root 'src\Yazaki.CommandeChaine.Launcher\bin\Release\net9.0-windows\Yazaki.CommandeChaine.Launcher.exe'

if (-not (Test-Path $launcherExe)) {
    throw "Release launcher not found: $launcherExe`nBuild it first: dotnet build Yazaki.CommandeChaine.slnx -c Release"
}

if ([string]::IsNullOrWhiteSpace($ShortcutPath)) {
    $desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
    $ShortcutPath = Join-Path $desktop 'CommandeChaine - Release.lnk'
}

$ws = New-Object -ComObject WScript.Shell
$lnk = $ws.CreateShortcut($ShortcutPath)
$lnk.TargetPath = $launcherExe
$lnk.WorkingDirectory = Split-Path $launcherExe -Parent
$lnk.IconLocation = "$launcherExe,0"
$lnk.Description = 'Yazaki CommandeChaine (Release)'
$lnk.Save()

Write-Host "Created shortcut: $ShortcutPath" -ForegroundColor Green
