# Verifies the host has everything it needs to build the ScamAlert WFP driver,
# and (if -CheckTestSigning) that the current machine has test-signing enabled
# (for VM use only - never enable on a workstation).
#
# Recognizes two valid WDK install paths:
#   1. Legacy: standalone wdksetup.exe drops headers/libs into
#      C:\Program Files (x86)\Windows Kits\10\Include\<ver>\km and
#      ...\Lib\<ver>\km\x64\fwpkclnt.lib
#   2. Modern: VS Installer "Windows Driver Kit" component installs the
#      Microsoft.Windows.DriverKit VSIX (project templates + MSBuild
#      targets). Kernel headers/libs come from the
#      Microsoft.Windows.WDK.x64 NuGet package referenced from the
#      .vcxproj at build time. The NuGet cache is populated on first
#      build, so before that the only on-disk evidence is the VSIX
#      under C:\ProgramData\Microsoft\VisualStudio\Packages\.
#
# Either path is sufficient. The script reports both and only fails
# when neither is present.
#
# Usage:
#   scripts/driver/check-driver-prereqs.ps1
#   scripts/driver/check-driver-prereqs.ps1 -CheckTestSigning

[CmdletBinding()]
param(
    [switch]$CheckTestSigning
)

$ErrorActionPreference = 'Stop'

# Path 1: legacy Windows Kits 10 km folder
$kitRoot     = 'C:\Program Files (x86)\Windows Kits\10'
$includeRoot = Join-Path $kitRoot 'Include'
$libRoot     = Join-Path $kitRoot 'Lib'

$fwpsk    = Get-ChildItem -Path $includeRoot -Recurse -Filter fwpsk.h    -ErrorAction SilentlyContinue | Select-Object -First 1
$ntddk    = Get-ChildItem -Path $includeRoot -Recurse -Filter ntddk.h    -ErrorAction SilentlyContinue | Select-Object -First 1
$fwpkclnt = Get-ChildItem -Path $libRoot     -Recurse -Filter fwpkclnt.lib -ErrorAction SilentlyContinue | Select-Object -First 1

$legacyWdkPresent = ($null -ne $ntddk) -and ($null -ne $fwpsk) -and ($null -ne $fwpkclnt)

# Path 2: VS Installer WDK extension + NuGet
$vsPkgRoot = 'C:\ProgramData\Microsoft\VisualStudio\Packages'
$wdkVsix = Get-ChildItem -Path $vsPkgRoot -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like 'Microsoft.Windows.DriverKit,version=*' } |
    Sort-Object Name -Descending | Select-Object -First 1
$wdkBuildVsix = Get-ChildItem -Path $vsPkgRoot -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like 'Microsoft.VisualStudio.WindowsDriverKit.Build,*' } |
    Sort-Object Name -Descending | Select-Object -First 1

# NuGet cache (populated on first build)
$nugetWdk = Get-ChildItem -Path "$env:USERPROFILE\.nuget\packages" -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like 'microsoft.windows.wdk.*' -or $_.Name -like 'microsoft.windows.wdk' } |
    Select-Object -First 5

$vsExtensionPresent = ($null -ne $wdkVsix) -and ($null -ne $wdkBuildVsix)

# Visual Studio detection
$vswhere  = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vsName   = $null
$vsPath   = $null
if (Test-Path -LiteralPath $vswhere) {
    $vsName = & $vswhere -latest -property displayName     -prerelease 2>$null
    $vsPath = & $vswhere -latest -property installationPath -prerelease 2>$null
}

$testSigning = $null
if ($CheckTestSigning.IsPresent) {
    $testSigning = ((bcdedit /enum '{current}' | Select-String -Pattern 'testsigning\s+Yes') -ne $null)
}

$result = [pscustomobject]@{
    WindowsKitsRoot       = (Test-Path -LiteralPath $kitRoot)
    LegacyWdkPresent      = $legacyWdkPresent
    NtddkHeader           = if ($ntddk)    { $ntddk.FullName }    else { '<missing - legacy path>' }
    FwpskHeader           = if ($fwpsk)    { $fwpsk.FullName }    else { '<missing - legacy path>' }
    FwpkclntLibrary       = if ($fwpkclnt) { $fwpkclnt.FullName } else { '<missing - legacy path>' }
    VsWdkExtensionPresent = $vsExtensionPresent
    VsWdkExtensionVersion = if ($wdkVsix) { ($wdkVsix.Name -replace '^Microsoft\.Windows\.DriverKit,version=','' -replace ',.*$','') } else { '<missing>' }
    NuGetWdkPackages      = if ($nugetWdk) { ($nugetWdk | ForEach-Object { $_.Name }) -join ', ' } else { '<none yet - will populate on first build>' }
    VisualStudio          = if ($vsName)   { "$vsName ($vsPath)" } else { '<missing>' }
    TestSigningChecked    = $CheckTestSigning.IsPresent
    TestSigningEnabled    = $testSigning
}

$result | Format-List

# A WDK is "ready" if either path is satisfied.
$wdkReady = $legacyWdkPresent -or $vsExtensionPresent

$missing = @()
if (-not $vsName)   { $missing += 'Visual Studio (vswhere did not find any installation)' }
if (-not $wdkReady) {
    $missing += 'Windows Driver Kit (neither legacy Windows Kits 10 km folder nor VS WDK extension is installed)'
}

if ($missing.Count -gt 0) {
    Write-Error ("Driver prerequisites missing: " + ($missing -join '; ') +
        ". Install the Windows Driver Kit (modern: VS Installer 'Windows Driver Kit' workload component; legacy: standalone wdksetup.exe).")
} else {
    if ($vsExtensionPresent -and -not $legacyWdkPresent) {
        Write-Host "Modern path: VS WDK extension is present. Kernel headers/libs will come from the Microsoft.Windows.WDK.x64 NuGet package referenced by the driver .vcxproj at build time." -ForegroundColor Green
    } elseif ($legacyWdkPresent) {
        Write-Host "Legacy path: kernel headers/libs are on disk under Windows Kits 10. The driver .vcxproj can use either NuGet or the global km folder." -ForegroundColor Green
    }
}
