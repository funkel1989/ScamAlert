# Creates the Hyper-V VM used for ScamAlert WFP driver testing.
#
# Run as administrator from the host. Idempotent on the VM name -
# it will refuse to overwrite an existing VM with the same name.
#
# Usage:
#   scripts/driver/create-dev-vm.ps1 `
#       -IsoPath  'C:\Users\funke\Downloads\26200.6584.250915-1905.25h2_ge_release_svc_refresh_CLIENTENTERPRISEEVAL_OEMRET_x64FRE_en-us.iso'
#
#   scripts/driver/create-dev-vm.ps1 `
#       -VmName   'ScamAlertDev' `
#       -VmRoot   'D:\HyperV\ScamAlertDev' `
#       -IsoPath  'D:\HyperV\ISOs\Win11.iso' `
#       -MemoryGB 8 `
#       -CpuCount 4 `
#       -VhdxSizeGB 80
#
# After the script finishes, click through the Windows installer in the
# vmconnect window. When Windows reaches the desktop, shut the VM down
# cleanly and run scripts/driver/finalize-dev-vm.ps1.

[CmdletBinding()]
param(
    [string]$VmName     = 'ScamAlertDev',
    [string]$VmRoot     = 'D:\HyperV\ScamAlertDev',
    [Parameter(Mandatory = $true)]
    [string]$IsoPath,
    [string]$SwitchName = 'Default Switch',
    [int]$MemoryGB      = 8,
    [int]$CpuCount      = 4,
    [int]$VhdxSizeGB    = 80
)

$ErrorActionPreference = 'Stop'

function Assert-Admin {
    $current = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($current)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "create-dev-vm.ps1 must be run from an elevated (admin) PowerShell."
    }
}

function Assert-IsoExists {
    if (-not (Test-Path -LiteralPath $IsoPath)) {
        throw "ISO not found at: $IsoPath"
    }
}

function Assert-HyperVReady {
    if (-not (Get-Command Get-VM -ErrorAction SilentlyContinue)) {
        throw "Hyper-V PowerShell module is not available. Enable Hyper-V first: Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V-All -All"
    }
    if (-not (Get-VMSwitch -Name $SwitchName -ErrorAction SilentlyContinue)) {
        throw "VM switch '$SwitchName' not found. Default Switch is created automatically when Hyper-V is enabled. List available switches with: Get-VMSwitch"
    }
}

function Assert-NameAvailable {
    if (Get-VM -Name $VmName -ErrorAction SilentlyContinue) {
        throw "VM '$VmName' already exists. Remove it first with: Remove-VM -Name $VmName -Force; Remove-Item -Recurse $VmRoot"
    }
}

Assert-Admin
Assert-IsoExists
Assert-HyperVReady
Assert-NameAvailable

Write-Host "Creating VM root at $VmRoot" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $VmRoot | Out-Null

$vhdxPath = Join-Path $VmRoot "$VmName.vhdx"

Write-Host "Creating dynamic VHDX ($VhdxSizeGB GB) at $vhdxPath" -ForegroundColor Cyan
New-VHD -Path $vhdxPath -Dynamic -SizeBytes ($VhdxSizeGB * 1GB) | Out-Null

Write-Host "Creating Generation 2 VM '$VmName'" -ForegroundColor Cyan
New-VM -Name $VmName `
       -MemoryStartupBytes ($MemoryGB * 1GB) `
       -Generation 2 `
       -VHDPath $vhdxPath `
       -SwitchName $SwitchName | Out-Null

Set-VMProcessor $VmName -Count $CpuCount
Set-VMMemory    $VmName -DynamicMemoryEnabled $false

Write-Host "Enabling vTPM (required by Win11 setup)" -ForegroundColor Cyan
Set-VMKeyProtector -VMName $VmName -NewLocalKeyProtector
Enable-VMTPM       -VMName $VmName

Write-Host "Mounting install ISO and forcing first boot from DVD" -ForegroundColor Cyan
Add-VMDvdDrive  -VMName $VmName -Path $IsoPath
$dvd = Get-VMDvdDrive -VMName $VmName
Set-VMFirmware  -VMName $VmName -FirstBootDevice $dvd

# Secure Boot must be ON for the Win11 install to pass hardware checks.
# We turn it OFF in finalize-dev-vm.ps1 once Windows is installed so
# test-signing will load unsigned kernel drivers.
Set-VMFirmware -VMName $VmName -EnableSecureBoot On

Write-Host "Starting VM and opening console" -ForegroundColor Cyan
Start-VM $VmName
vmconnect.exe $env:COMPUTERNAME $VmName

Write-Host ""
Write-Host "VM '$VmName' is up. Click through the Windows installer:" -ForegroundColor Green
Write-Host "  - Pick 'Windows 11 Enterprise'."
Write-Host "  - Custom install -> use the entire $VhdxSizeGB GB drive."
Write-Host "  - When the OOBE asks for an account, pick 'domain join' / 'sign-in options' to land on a local-account-only flow."
Write-Host "  - Username 'dev', any password you'll remember."
Write-Host ""
Write-Host "When Windows reaches the desktop, shut the VM down cleanly (Start > Power > Shut down)" -ForegroundColor Yellow
Write-Host "Then run: scripts/driver/finalize-dev-vm.ps1 -VmName $VmName" -ForegroundColor Yellow
