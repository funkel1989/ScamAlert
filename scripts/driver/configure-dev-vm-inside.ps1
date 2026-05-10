# Run THIS SCRIPT INSIDE THE VM as administrator after Windows install
# completes and finalize-dev-vm.ps1 has flipped Secure Boot off.
#
# Enables test-signing, PowerShell remoting, sets a stable computer name,
# then reboots so the host can drive the VM via Invoke-Command -VMName.
#
# Usage (inside the VM, admin PowerShell):
#   .\configure-dev-vm-inside.ps1
#   .\configure-dev-vm-inside.ps1 -ComputerName ScamAlertDev

[CmdletBinding()]
param(
    [string]$ComputerName = 'ScamAlertDev'
)

$ErrorActionPreference = 'Stop'

function Assert-Admin {
    $current = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($current)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "configure-dev-vm-inside.ps1 must be run from an elevated (admin) PowerShell."
    }
}

Assert-Admin

Write-Host "Enabling test-signing" -ForegroundColor Cyan
bcdedit /set testsigning on | Out-Null

Write-Host "Enabling PowerShell remoting" -ForegroundColor Cyan
Enable-PSRemoting -Force -SkipNetworkProfileCheck | Out-Null
Set-NetFirewallRule -Name 'WINRM-HTTP-In-TCP' -Enabled True -ErrorAction SilentlyContinue | Out-Null

Write-Host "Trusting any host as a remoting client (overridable later)" -ForegroundColor Cyan
Set-Item WSMan:\localhost\Client\TrustedHosts -Value '*' -Force

if ($env:COMPUTERNAME -ne $ComputerName) {
    Write-Host "Renaming computer from $env:COMPUTERNAME to $ComputerName" -ForegroundColor Cyan
    Rename-Computer -NewName $ComputerName -Force
} else {
    Write-Host "Computer name already $ComputerName - skipping rename" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "Done. Rebooting in 10 seconds. After reboot:" -ForegroundColor Green
Write-Host "  - 'Test Mode' watermark should appear in the lower-right of the desktop."
Write-Host "  - On the host, this should now succeed:" -ForegroundColor Yellow
Write-Host "      `$cred = Get-Credential   # the local admin user inside the VM"
Write-Host "      Invoke-Command -VMName $ComputerName -Credential `$cred -ScriptBlock { `$env:COMPUTERNAME }"

Start-Sleep -Seconds 10
Restart-Computer -Force
