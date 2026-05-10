# Builds native/ScamAlert.WfpDriver/ScamAlert.WfpDriver.vcxproj.
#
# Run from a Visual Studio Developer PowerShell (or any shell where
# msbuild and nuget are on PATH). On a stock VS install, the easiest
# entry point is "Developer PowerShell for VS 2026" from the Start menu.
#
# Usage:
#   scripts/driver/build-driver.ps1
#   scripts/driver/build-driver.ps1 -Configuration Release
#   scripts/driver/build-driver.ps1 -SkipRestore   # skip nuget restore (faster repeat builds)

[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipRestore
)

$ErrorActionPreference = 'Stop'

$vcxproj = Join-Path $PSScriptRoot '..\..\native\ScamAlert.WfpDriver\ScamAlert.WfpDriver.vcxproj'
$vcxproj = (Resolve-Path $vcxproj).Path

$msbuild = Get-Command msbuild -ErrorAction SilentlyContinue
if (-not $msbuild) {
    throw "msbuild is not on PATH. Open 'Developer PowerShell for VS 2026' from the Start menu and re-run."
}

if (-not $SkipRestore) {
    $nuget = Get-Command nuget -ErrorAction SilentlyContinue
    if ($nuget) {
        Write-Host "Restoring NuGet packages via nuget.exe" -ForegroundColor Cyan
        & $nuget.Source restore (Join-Path (Split-Path $vcxproj -Parent) 'packages.config') `
            -PackagesDirectory (Join-Path (Split-Path $vcxproj -Parent) '..\packages')
    } else {
        Write-Host "Restoring NuGet packages via msbuild /t:Restore" -ForegroundColor Cyan
        & msbuild $vcxproj /t:Restore /p:Configuration=$Configuration /p:Platform=x64
    }
}

Write-Host "Building $Configuration|x64" -ForegroundColor Cyan
& msbuild $vcxproj /p:Configuration=$Configuration /p:Platform=x64 /m

if ($LASTEXITCODE -ne 0) {
    throw "Driver build failed with exit code $LASTEXITCODE."
}

$outDir = Join-Path (Split-Path $vcxproj -Parent) "..\..\build\$Configuration\x64\ScamAlert.WfpDriver"
$outDir = (Resolve-Path $outDir -ErrorAction SilentlyContinue)
if ($outDir) {
    Write-Host ""
    Write-Host "Build artifacts in $outDir" -ForegroundColor Green
    Get-ChildItem $outDir -Filter 'ScamAlert.WfpDriver.*' | Select-Object Name, Length | Format-Table -AutoSize
}
