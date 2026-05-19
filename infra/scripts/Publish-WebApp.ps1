#Requires -Version 7.0
<#
.SYNOPSIS
  Publishes ScamAlert.Api to an Azure App Service (zip deploy).

.EXAMPLE
  ./Publish-WebApp.ps1 -ResourceGroupName rg-scamalert-staging -WebAppName app-scamalert-staging-abc123
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ResourceGroupName,

    [Parameter(Mandatory = $true)]
    [string] $WebAppName,

    [string] $Configuration = 'Release',

    [string] $RepoRoot = ''
)

$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '../../..')
}

$apiProject = Join-Path $RepoRoot 'src/ScamAlert.Api/ScamAlert.Api.csproj'
$publishDir = Join-Path $RepoRoot 'artifacts/publish/api'
$zipPath = Join-Path $RepoRoot 'artifacts/publish/api.zip'

if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}
New-Item -ItemType Directory -Force -Path (Split-Path $zipPath) | Out-Null

Write-Host "Publishing $apiProject..."
dotnet publish $apiProject -c $Configuration -o $publishDir --no-self-contained

if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath

Write-Host "Deploying to App Service $WebAppName..."
az webapp deploy `
    --resource-group $ResourceGroupName `
    --name $WebAppName `
    --src-path $zipPath `
    --type zip `
    --async false

Write-Host "Done. Site: https://$WebAppName.azurewebsites.net"
