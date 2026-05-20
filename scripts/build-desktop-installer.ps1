#Requires -Version 5.1
<#
.SYNOPSIS
  Builds the ScamAlert desktop MSI (Broker Windows service + Tray startup shortcut).

.EXAMPLE
  .\scripts\build-desktop-installer.ps1
  .\scripts\build-desktop-installer.ps1 -Configuration Debug
#>
param(
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$wixproj = Join-Path $repoRoot 'installer\ScamAlert.DesktopInstaller\ScamAlert.DesktopInstaller.wixproj'

Write-Host "Publishing Broker and Tray (win-x64, self-contained)..." -ForegroundColor Cyan
dotnet publish (Join-Path $repoRoot 'src\ScamAlert.Broker\ScamAlert.Broker.csproj') `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishReadyToRun=true `
    -o (Join-Path $repoRoot 'artifacts\installer\broker')

dotnet publish (Join-Path $repoRoot 'src\ScamAlert.Tray\ScamAlert.Tray.csproj') `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishReadyToRun=true `
    -o (Join-Path $repoRoot 'artifacts\installer\tray')

Write-Host "Building MSI..." -ForegroundColor Cyan
dotnet build $wixproj -c $Configuration

$msiDir = Join-Path $repoRoot "installer\ScamAlert.DesktopInstaller\bin\$Configuration"
$msi = Get-ChildItem -Path $msiDir -Filter '*.msi' -Recurse | Select-Object -First 1
if ($null -eq $msi) {
    throw "MSI not found under $msiDir"
}

Write-Host "Installer: $($msi.FullName)" -ForegroundColor Green
Write-Host "After install: pair the PC from the portal (Devices -> Pair PC), then run:" -ForegroundColor Yellow
Write-Host "  .\scripts\configure-broker-from-pairing-code.ps1 -ApiBaseUrl <url> -PairingCode <code>" -ForegroundColor Yellow
