#Requires -Version 5.1
<#
.SYNOPSIS
  Redeems a portal pairing code and writes broker cloud config to ProgramData.

.PARAMETER ApiBaseUrl
  Public API root (e.g. https://app.scamalert.com).

.PARAMETER PairingCode
  8-character code from the portal Devices page.

.EXAMPLE
  .\configure-broker-from-pairing-code.ps1 -ApiBaseUrl "https://localhost:7091" -PairingCode "AB12CD34"
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $ApiBaseUrl,

    [Parameter(Mandatory = $true)]
    [string] $PairingCode
)

$ErrorActionPreference = "Stop"
$uri = ($ApiBaseUrl.TrimEnd("/")) + "/api/setup/redeem"
$body = @{ code = $PairingCode.Trim().ToUpperInvariant() } | ConvertTo-Json

$response = Invoke-RestMethod -Method Post -Uri $uri -ContentType "application/json" -Body $body
$configDir = Join-Path $env:ProgramData "ScamAlert"
$configPath = Join-Path $configDir "broker.appsettings.json"
New-Item -ItemType Directory -Path $configDir -Force | Out-Null

$payload = @{
    CloudAlerts = @{
        Enabled = $true
        BaseUrl = $response.apiBaseUrl
        ExternalDeviceId = $response.externalDeviceId
        DeviceIngestApiKey = $response.deviceIngestApiKey
    }
} | ConvertTo-Json -Depth 4

Set-Content -Path $configPath -Value $payload -Encoding UTF8
Write-Host "Wrote broker config for '$($response.deviceName)' to $configPath" -ForegroundColor Green
Write-Host "Restart ScamAlert.Broker (and Tray) to apply." -ForegroundColor Yellow
