# Enables RDP in the dev VM so port 3389 has a real listener and the
# Windows firewall lets inbound 3389 in. Without a listener,
# FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V4 never fires (the kernel RSTs the
# inbound SYN before reaching ALE), so the probe sees nothing even
# though the driver is loaded.
#
# Run from the host. Invokes a script block in the VM via PowerShell
# Direct.
#
# Usage:
#   scripts/driver/prep-vm-for-traffic.ps1
#   scripts/driver/prep-vm-for-traffic.ps1 -VmName ScamAlertDev -UserName dev

[CmdletBinding()]
param(
    [string]$VmName   = 'ScamAlertDev',
    [string]$UserName = 'dev'
)

$ErrorActionPreference = 'Stop'

$cred = Get-Credential -UserName $UserName -Message "Local admin password for $VmName"

$report = Invoke-Command -VMName $VmName -Credential $cred -ScriptBlock {
    # Turn on Remote Desktop (gives us a real listener on 3389).
    Set-ItemProperty -Path 'HKLM:\System\CurrentControlSet\Control\Terminal Server' `
                     -Name 'fDenyTSConnections' -Value 0

    # Open inbound 3389 in Windows Defender Firewall.
    Enable-NetFirewallRule -DisplayGroup "Remote Desktop" -ErrorAction SilentlyContinue

    # Make sure the service is up and starts automatically.
    Set-Service -Name TermService -StartupType Automatic
    if ((Get-Service TermService).Status -ne 'Running') {
        Start-Service TermService
    }

    # Verify a listener is up and the firewall is open.
    [pscustomobject]@{
        TermService     = (Get-Service TermService).Status
        FirewallRules   = (Get-NetFirewallRule -DisplayGroup "Remote Desktop" |
                              Select-Object -First 1 -ExpandProperty Enabled)
        Listening3389   = ($null -ne (Get-NetTCPConnection -LocalPort 3389 -State Listen -ErrorAction SilentlyContinue))
        VmIPv4          = (Get-NetIPAddress -AddressFamily IPv4 |
                              Where-Object { $_.InterfaceAlias -notmatch 'Loopback' -and $_.IPAddress -notmatch '^169\.' } |
                              Select-Object -First 1 -ExpandProperty IPAddress)
    }
}

$report | Format-List

if (-not $report.Listening3389) {
    Write-Warning "Nothing is listening on 3389 yet. TermService may need a moment to bind. Wait 3-5s and re-check via Get-NetTCPConnection -LocalPort 3389 inside the VM."
} else {
    Write-Host ""
    Write-Host "VM is ready to receive inbound RDP at $($report.VmIPv4):3389." -ForegroundColor Green
    Write-Host "From the host:" -ForegroundColor Green
    Write-Host "  Test-NetConnection -ComputerName $($report.VmIPv4) -Port 3389"
}
