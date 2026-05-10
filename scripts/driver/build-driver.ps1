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

function Resolve-MSBuild {
    $cmd = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path -LiteralPath $vswhere)) {
        throw "Could not find vswhere.exe. Open 'Developer PowerShell for VS 2026' from the Start menu and re-run."
    }

    $candidates = @(
        & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\amd64\MSBuild.exe' 2>$null
        & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1

    if (-not $candidates) {
        throw "Could not locate msbuild via vswhere. Open 'Developer PowerShell for VS 2026' and re-run."
    }
    return $candidates
}

$msbuild = Resolve-MSBuild
Write-Host "Using msbuild: $msbuild" -ForegroundColor DarkGray

if (-not $SkipRestore) {
    Write-Host "Restoring NuGet packages via msbuild /t:Restore" -ForegroundColor Cyan
    & $msbuild $vcxproj /t:Restore /p:Configuration=$Configuration /p:Platform=x64 /v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet restore failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Building $Configuration|x64" -ForegroundColor Cyan
& $msbuild $vcxproj /p:Configuration=$Configuration /p:Platform=x64 /m /v:minimal

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
