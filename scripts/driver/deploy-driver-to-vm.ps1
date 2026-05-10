# Copies ScamAlert.WfpDriver.sys into the dev VM, installs it as a
# kernel-mode service via sc create, and starts it. Test-signing must
# be enabled inside the VM (configure-dev-vm-inside.ps1 takes care of
# that).
#
# Run on the host. Prompts for the VM's local admin credential.
#
# Usage:
#   scripts/driver/deploy-driver-to-vm.ps1
#   scripts/driver/deploy-driver-to-vm.ps1 -Configuration Release -VmName ScamAlertDev -UserName dev

[CmdletBinding()]
param(
    [string]$VmName        = 'ScamAlertDev',
    [string]$UserName      = 'dev',
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Debug',
    [string]$ServiceName   = 'ScamAlertWfp'
)

$ErrorActionPreference = 'Stop'

$sysPath = Join-Path $PSScriptRoot "..\..\native\ScamAlert.WfpDriver\build\$Configuration\x64\ScamAlert.WfpDriver\ScamAlert.WfpDriver.sys"
$sysPath = (Resolve-Path $sysPath -ErrorAction Stop).Path
Write-Host "Source .sys: $sysPath" -ForegroundColor Cyan

$cred = Get-Credential -UserName $UserName -Message "Local admin password for $VmName"

$session = New-PSSession -VMName $VmName -Credential $cred
try {
    $vmStagingDir = 'C:\ScamAlertWfp'
    $vmSysPath    = "$vmStagingDir\ScamAlert.WfpDriver.sys"

    Write-Host "Stopping any prior service inside the VM" -ForegroundColor Cyan
    $stopReport = Invoke-Command -Session $session -ScriptBlock {
        param($svc, $sysOnVm)

        # Stop + delete (idempotent).
        sc.exe stop   $svc 2>&1 | Out-Null
        sc.exe delete $svc 2>&1 | Out-Null

        # Poll until either:
        #  (a) sc query reports no such service, AND
        #  (b) the existing .sys can be opened for write (i.e. no longer
        #      mapped by the kernel).
        $deadline = (Get-Date).AddSeconds(20)
        $unloaded = $false
        while ((Get-Date) -lt $deadline) {
            sc.exe query $svc 2>&1 | Out-Null
            $serviceGone = ($LASTEXITCODE -ne 0)

            $fileWritable = $true
            if (Test-Path -LiteralPath $sysOnVm) {
                try {
                    $fs = [System.IO.File]::Open($sysOnVm, 'Open', 'Write', 'None')
                    $fs.Close()
                } catch {
                    $fileWritable = $false
                }
            }

            if ($serviceGone -and $fileWritable) {
                $unloaded = $true
                break
            }
            Start-Sleep -Milliseconds 250
        }

        [pscustomobject]@{
            Unloaded   = $unloaded
            ServiceGone = ($LASTEXITCODE -ne 0)
            ExistingSysAccessible = (-not (Test-Path -LiteralPath $sysOnVm)) -or {
                try { $fs = [System.IO.File]::Open($sysOnVm, 'Open', 'Write', 'None'); $fs.Close(); $true } catch { $false }
            }.Invoke()
        }
    } -ArgumentList $ServiceName, $vmSysPath

    if (-not $stopReport.Unloaded) {
        throw "Driver did not unload within 20 s. The kernel is still holding $vmSysPath. Reboot the VM with 'Restart-VM $VmName -Force' and re-run."
    }

    Write-Host "Creating staging directory in VM: $vmStagingDir" -ForegroundColor Cyan
    Invoke-Command -Session $session -ScriptBlock {
        param($dir)
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    } -ArgumentList $vmStagingDir

    $sysSizeKB = [math]::Round((Get-Item $sysPath).Length / 1KB, 1)
    Write-Host "Copying .sys into VM ($sysSizeKB KB)" -ForegroundColor Cyan
    Copy-Item -ToSession $session -Path $sysPath -Destination $vmSysPath -Force

    Write-Host "Registering service '$ServiceName' inside VM" -ForegroundColor Cyan
    $result = Invoke-Command -Session $session -ScriptBlock {
        param($svc, $bin)
        $create = sc.exe create $svc type= kernel start= demand binPath= $bin DisplayName= "ScamAlert WFP Monitor"
        $start  = sc.exe start  $svc
        $query  = sc.exe query  $svc

        [pscustomobject]@{
            Create = ($create -join "`n")
            Start  = ($start  -join "`n")
            Query  = ($query  -join "`n")
        }
    } -ArgumentList $ServiceName, $vmSysPath

    Write-Host ""
    Write-Host "--- sc create ---" -ForegroundColor Yellow
    Write-Host $result.Create
    Write-Host "--- sc start ---" -ForegroundColor Yellow
    Write-Host $result.Start
    Write-Host "--- sc query ---" -ForegroundColor Yellow
    Write-Host $result.Query

    if ($result.Query -match 'STATE\s+:\s+\d+\s+RUNNING') {
        Write-Host ""
        Write-Host "Driver service '$ServiceName' is RUNNING in $VmName." -ForegroundColor Green
        Write-Host "Verify the device exists from any user-mode caller:" -ForegroundColor Green
        Write-Host "  Test-Path '\\\\.\\ScamAlertWfp'   # inside the VM"
    } else {
        Write-Warning "Service did not reach RUNNING state. Check the VM Event Viewer (System log) for driver load failures."
    }
}
finally {
    Remove-PSSession $session
}
