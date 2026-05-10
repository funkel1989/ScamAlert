# Confirms the host can reach the dev VM via PowerShell Direct
# (Invoke-Command -VMName), and reports the VM's test-signing state,
# network profile, and computer name.
#
# Run on the host. Will prompt for the VM's local admin credential
# (default username 'dev').
#
# Usage:
#   scripts/driver/verify-vm-access.ps1
#   scripts/driver/verify-vm-access.ps1 -VmName ScamAlertDev -UserName dev

[CmdletBinding()]
param(
    [string]$VmName   = 'ScamAlertDev',
    [string]$UserName = 'dev'
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command Get-VM -ErrorAction SilentlyContinue)) {
    throw "Hyper-V module not available. Run from an admin PowerShell on a host with Hyper-V enabled."
}

$vm = Get-VM -Name $VmName -ErrorAction SilentlyContinue
if (-not $vm) { throw "VM '$VmName' not found." }
if ($vm.State -ne 'Running') {
    Write-Host "VM '$VmName' is $($vm.State). Starting it..." -ForegroundColor Cyan
    Start-VM $VmName
    Start-Sleep -Seconds 5
}

$cred = Get-Credential -UserName $UserName -Message "Local admin password for $VmName"

Write-Host "Probing $VmName via PowerShell Direct..." -ForegroundColor Cyan
$report = Invoke-Command -VMName $VmName -Credential $cred -ScriptBlock {
    [pscustomobject]@{
        Computer       = $env:COMPUTERNAME
        OSVersion      = (Get-CimInstance Win32_OperatingSystem).Version
        OSCaption      = (Get-CimInstance Win32_OperatingSystem).Caption
        TestMode       = ((bcdedit /enum '{current}' | Select-String -Pattern 'testsigning\s+Yes') -ne $null)
        NetworkProfile = (Get-NetConnectionProfile | Select-Object -First 1).NetworkCategory
        WhoAmI         = (whoami)
        WinRMService   = (Get-Service WinRM).Status
        SecureBoot     = if (Confirm-SecureBootUEFI -ErrorAction SilentlyContinue) { 'On' } else { 'Off' }
    }
}

$report | Format-List

$problems = @()
if ($report.Computer -ne $VmName)          { $problems += "Computer is '$($report.Computer)', expected '$VmName' - rerun Rename-Computer in the VM." }
if (-not $report.TestMode)                 { $problems += "Test-signing is OFF - run 'bcdedit /set testsigning on' in the VM and reboot." }
if ($report.SecureBoot -eq 'On')           { $problems += "Secure Boot is ON - run finalize-dev-vm.ps1 to flip it off." }
if ($report.NetworkProfile -eq 'Public')   { $problems += "Network profile is Public - run Set-NetConnectionProfile -NetworkCategory Private." }

if ($problems.Count -gt 0) {
    Write-Host "" -NoNewline
    Write-Warning ("VM is reachable but not fully configured: " + ($problems -join '; '))
    exit 1
}

Write-Host ""
Write-Host "All checks passed. Host -> VM PowerShell Direct works. The VM is ready for driver install." -ForegroundColor Green
