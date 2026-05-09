# Verifies the host has everything it needs to build the ScamAlert WFP driver,
# and (if -CheckTestSigning) that the current machine has test-signing enabled
# (for VM use only - never enable on a workstation).
#
# Usage:
#   scripts/driver/check-driver-prereqs.ps1
#   scripts/driver/check-driver-prereqs.ps1 -CheckTestSigning

[CmdletBinding()]
param(
    [switch]$CheckTestSigning
)

$ErrorActionPreference = 'Stop'

$kitRoot     = 'C:\Program Files (x86)\Windows Kits\10'
$includeRoot = Join-Path $kitRoot 'Include'
$libRoot     = Join-Path $kitRoot 'Lib'

$fwpsk    = Get-ChildItem -Path $includeRoot -Recurse -Filter fwpsk.h    -ErrorAction SilentlyContinue | Select-Object -First 1
$ntddk    = Get-ChildItem -Path $includeRoot -Recurse -Filter ntddk.h    -ErrorAction SilentlyContinue | Select-Object -First 1
$fwpkclnt = Get-ChildItem -Path $libRoot     -Recurse -Filter fwpkclnt.lib -ErrorAction SilentlyContinue | Select-Object -First 1

$vswhere  = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vsName   = $null
$vsPath   = $null
if (Test-Path -LiteralPath $vswhere) {
    $vsName = & $vswhere -latest -property displayName 2>$null
    $vsPath = & $vswhere -latest -property installationPath 2>$null
}

$testSigning = $null
if ($CheckTestSigning.IsPresent) {
    $testSigning = ((bcdedit /enum '{current}' | Select-String -Pattern 'testsigning\s+Yes') -ne $null)
}

$result = [pscustomobject]@{
    WindowsKitsRoot      = (Test-Path -LiteralPath $kitRoot)
    NtddkHeader          = if ($ntddk)    { $ntddk.FullName }    else { '<missing>' }
    FwpskHeader          = if ($fwpsk)    { $fwpsk.FullName }    else { '<missing>' }
    FwpkclntLibrary      = if ($fwpkclnt) { $fwpkclnt.FullName } else { '<missing>' }
    VisualStudio         = if ($vsName)   { "$vsName ($vsPath)" } else { '<missing>' }
    TestSigningChecked   = $CheckTestSigning.IsPresent
    TestSigningEnabled   = $testSigning
}

$result | Format-List

$missing = @()
if (-not $ntddk)    { $missing += 'ntddk.h (WDK kernel headers)' }
if (-not $fwpsk)    { $missing += 'fwpsk.h (WDK WFP kernel headers)' }
if (-not $fwpkclnt) { $missing += 'fwpkclnt.lib (WDK WFP kernel library)' }
if (-not $vsName)   { $missing += 'Visual Studio (vswhere did not find any installation)' }

if ($missing.Count -gt 0) {
    Write-Error ("Driver prerequisites missing: " + ($missing -join '; ') +
        ". Install the Windows Driver Kit matching SDK 10.0.26100.0 and the VS C++ Spectre-mitigated libraries before building ScamAlert.WfpDriver.")
}
