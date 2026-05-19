#Requires -Version 7.0
<#
.SYNOPSIS
  Deploys ScamAlert Azure infrastructure (Phase 1) into a resource group.

.EXAMPLE
  ./Deploy-Infrastructure.ps1 -ResourceGroupName rg-scamalert-staging -SqlAdminPassword (Read-Host -AsSecureString)

.EXAMPLE
  ./Deploy-Infrastructure.ps1 -ResourceGroupName rg-scamalert-staging -ParametersFile ../main.parameters.staging.local.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ResourceGroupName,

    [string] $Location = 'eastus2',

    [string] $ParametersFile = '',

    [SecureString] $SqlAdminPassword,

    [string] $EnvironmentName = 'staging'
)

$ErrorActionPreference = 'Stop'
$infraRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI (az) is required. Install from https://learn.microsoft.com/cli/azure/install-azure-cli'
}

$rgExists = az group exists --name $ResourceGroupName | ConvertFrom-Json
if (-not $rgExists) {
    Write-Host "Creating resource group $ResourceGroupName in $Location..."
    az group create --name $ResourceGroupName --location $Location | Out-Null
}

$deploymentParams = @(
    'deployment', 'group', 'create',
    '--resource-group', $ResourceGroupName,
    '--template-file', (Join-Path $infraRoot 'main.bicep'),
    '--parameters', "environmentName=$EnvironmentName", "location=$Location"
)

if ($ParametersFile) {
    $deploymentParams += '--parameters'
    $deploymentParams += "@$ParametersFile"
}

if ($SqlAdminPassword) {
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SqlAdminPassword)
    try {
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
    $deploymentParams += '--parameters'
    $deploymentParams += "sqlAdminPassword=$plain"
}

Write-Host "Deploying Bicep to $ResourceGroupName..."
az @deploymentParams

Write-Host ''
Write-Host 'Deployment outputs:'
az deployment group show `
    --resource-group $ResourceGroupName `
    --name (az deployment group list --resource-group $ResourceGroupName --query '[0].name' -o tsv) `
    --query 'properties.outputs' `
    -o json
