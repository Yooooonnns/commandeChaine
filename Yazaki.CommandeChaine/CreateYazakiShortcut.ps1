param(
    [string]$ShortcutPath = "$( [Environment]::GetFolderPath('Desktop') )\Yazaki.CommandeChaine.Launcher.lnk",
    [string]$DesktopPath = ""
)

$ErrorActionPreference = 'Stop'

if (![string]::IsNullOrWhiteSpace($DesktopPath)) {
    $ShortcutPath = Join-Path $DesktopPath 'Yazaki.CommandeChaine.Launcher.lnk'
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$target = Join-Path $root 'src\Yazaki.CommandeChaine.Launcher\bin\Release\net9.0-windows\publish\Yazaki.CommandeChaine.Launcher.exe'

if (!(Test-Path $target)) {
    throw "Launcher publish executable not found at: $target. Publish first with: dotnet publish src/Yazaki.CommandeChaine.Launcher/Yazaki.CommandeChaine.Launcher.csproj -c Release -o src/Yazaki.CommandeChaine.Launcher/bin/Release/net9.0-windows/publish"
}

$wsh = New-Object -ComObject WScript.Shell
$shortcut = $wsh.CreateShortcut($ShortcutPath)
$shortcut.TargetPath = $target
$shortcut.WorkingDirectory = Split-Path -Parent $target
$shortcut.WindowStyle = 1
$shortcut.Description = 'Lance Yazaki Commande Chaine (derniere version publiee)'
$shortcut.IconLocation = "$env:SystemRoot\System32\shell32.dll,167"
$shortcut.Save()

Write-Host "Shortcut created: $ShortcutPath"
Write-Host "Target: $target"
