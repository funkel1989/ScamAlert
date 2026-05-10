# Finalizes the ScamAlert dev VM after Windows install completes:
#   - Detaches the install ISO so the VM stops booting from DVD.
#   - Turns Secure Boot off (required for test-signing to load unsigned drivers).
#   - Restarts the VM and opens vmconnect.
#
# Run as administrator on the host. The VM should be powered OFF
# before running this.
#
# Inside the VM, after this script runs and Windows comes back up,
# open admin PowerShell in the VM and run scripts/driver/configure-dev-vm-inside.ps1
# (which is a thin wrapper around bcdedit / Enable-PSRemoting / Rename-Computer).

[CmdletBinding()]
param(
    [string]$VmName = 'ScamAlertDev'
)

$ErrorActionPreference = 'Stop'

function Assert-Admin {
    $current = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($current)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "finalize-dev-vm.ps1 must be run from an elevated (admin) PowerShell."
    }
}

Assert-Admin

$vm = Get-VM -Name $VmName -ErrorAction SilentlyContinue
if (-not $vm) { throw "VM '$VmName' not found. Run create-dev-vm.ps1 first." }
if ($vm.State -ne 'Off') {
    throw "VM '$VmName' is currently $($vm.State). Shut it down cleanly from inside Windows first, then re-run."
}

Write-Host "Detaching install ISO" -ForegroundColor Cyan
Get-VMDvdDrive -VMName $VmName | Where-Object { $_.Path } | ForEach-Object {
    Set-VMDvdDrive -VMName $VmName -ControllerNumber $_.ControllerNumber -ControllerLocation $_.ControllerLocation -Path $null
}

Write-Host "Turning Secure Boot off (required for test-signing)" -ForegroundColor Cyan
Set-VMFirmware -VMName $VmName -EnableSecureBoot Off

Write-Host "Starting VM" -ForegroundColor Cyan
Start-VM $VmName
vmconnect.exe $env:COMPUTERNAME $VmName

Write-Host ""
Write-Host "VM '$VmName' is back up with Secure Boot off." -ForegroundColor Green
Write-Host ""
Write-Host "Inside the VM, open admin PowerShell and run:" -ForegroundColor Yellow
Write-Host "  bcdedit /set testsigning on"
Write-Host "  Enable-PSRemoting -Force"
Write-Host "  Set-Item WSMan:\localhost\Client\TrustedHosts -Value '*' -Force"
Write-Host "  Rename-Computer -NewName $VmName -Force"
Write-Host "  Restart-Computer"
Write-Host ""
Write-Host "After the VM reboots you should see 'Test Mode' in the lower-right corner of the desktop."
