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
    [string] $Configuration = 'Release',

    # Baked into HKLM\Software\ScamAlert\ApiBaseUrl so the pairing wizard only asks for the code.
    # Defaults to Web:PublicBaseUrl from appsettings when not localhost; override for production builds.
    [string] $ApiBaseUrl = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$wixproj = Join-Path $repoRoot 'installer\ScamAlert.DesktopInstaller\ScamAlert.DesktopInstaller.wixproj'

Write-Host "Publishing Broker, Tray, and Configurator (win-x64, self-contained)..." -ForegroundColor Cyan
dotnet publish (Join-Path $repoRoot 'src\ScamAlert.Broker\ScamAlert.Broker.csproj') `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishReadyToRun=true `
    -o (Join-Path $repoRoot 'artifacts\installer\broker')

dotnet publish (Join-Path $repoRoot 'src\ScamAlert.Tray\ScamAlert.Tray.csproj') `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishReadyToRun=true `
    -o (Join-Path $repoRoot 'artifacts\installer\tray')

dotnet publish (Join-Path $repoRoot 'src\ScamAlert.Configurator\ScamAlert.Configurator.csproj') `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishReadyToRun=true `
    -o (Join-Path $repoRoot 'artifacts\installer\configurator')

if (-not $ApiBaseUrl) {
    $appsettingsPath = Join-Path $repoRoot 'src\ScamAlert.Api\appsettings.json'
    if (Test-Path $appsettingsPath) {
        $web = (Get-Content $appsettingsPath -Raw | ConvertFrom-Json).Web
        if ($web.PublicBaseUrl -and $web.PublicBaseUrl -notmatch 'localhost') {
            $ApiBaseUrl = $web.PublicBaseUrl.Trim().TrimEnd('/')
        }
    }
}

if ($ApiBaseUrl) {
    Write-Host "Baking API URL into installer: $ApiBaseUrl" -ForegroundColor Cyan
}

Write-Host "Building MSI..." -ForegroundColor Cyan
$msbuildArgs = @(
    $wixproj,
    '-c', $Configuration,
    "-p:DefaultApiBaseUrl=$ApiBaseUrl"
)
dotnet build @msbuildArgs

$msiDir = Join-Path $repoRoot "installer\ScamAlert.DesktopInstaller\bin\$Configuration"
$msi = Get-ChildItem -Path $msiDir -Filter '*.msi' -Recurse | Select-Object -First 1
if ($null -eq $msi) {
    throw "MSI not found under $msiDir"
}

Write-Host "Installer: $($msi.FullName)" -ForegroundColor Green
Write-Host "After install: the Pair this PC wizard opens automatically (or Start Menu -> ScamAlert)." -ForegroundColor Yellow
