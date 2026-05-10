# Builds the user-mode services (Broker + DriverBridge) self-contained
# for win-x64 and pushes them into the dev VM. Stops any prior instances
# inside the VM, copies the publish output, then starts both services as
# detached processes.
#
# Run on the host. Prompts for the VM dev user credential.
#
# Usage:
#   scripts/driver/deploy-userland-to-vm.ps1
#   scripts/driver/deploy-userland-to-vm.ps1 -Configuration Release -SkipBuild
#
# After this finishes, generate inbound RDP from another machine
# (Test-NetConnection -ComputerName <vm-ip> -Port 3389) and inspect
# the broker's signals.jsonl inside the VM:
#
#   Get-Content -Wait -Path "$env:LocalAppData\ScamAlert\signals.jsonl"

[CmdletBinding()]
param(
    [string]$VmName        = 'ScamAlertDev',
    [string]$UserName      = 'dev',
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$brokerProj = Join-Path $repoRoot 'src\ScamAlert.Broker\ScamAlert.Broker.csproj'
$bridgeProj = Join-Path $repoRoot 'src\ScamAlert.DriverBridge\ScamAlert.DriverBridge.csproj'

$brokerOut = Join-Path $repoRoot 'build\publish\ScamAlert.Broker'
$bridgeOut = Join-Path $repoRoot 'build\publish\ScamAlert.DriverBridge'

if (-not $SkipBuild) {
    Write-Host "Publishing ScamAlert.Broker (self-contained, win-x64) ..." -ForegroundColor Cyan
    dotnet publish $brokerProj -c $Configuration -r win-x64 --self-contained true -o $brokerOut --nologo /clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) { throw "Broker publish failed." }

    Write-Host "Publishing ScamAlert.DriverBridge (self-contained, win-x64) ..." -ForegroundColor Cyan
    dotnet publish $bridgeProj -c $Configuration -r win-x64 --self-contained true -o $bridgeOut --nologo /clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) { throw "Bridge publish failed." }
}

if (-not (Test-Path $brokerOut)) { throw "Broker publish output not found at $brokerOut. Re-run without -SkipBuild." }
if (-not (Test-Path $bridgeOut)) { throw "Bridge publish output not found at $bridgeOut. Re-run without -SkipBuild." }

$cred    = Get-Credential -UserName $UserName -Message "Local admin password for $VmName"
$session = New-PSSession -VMName $VmName -Credential $cred

try {
    Write-Host "Stopping any prior Broker/Bridge processes inside the VM" -ForegroundColor Cyan
    Invoke-Command -Session $session -ScriptBlock {
        @('ScamAlert.Broker','ScamAlert.DriverBridge') | ForEach-Object {
            Get-Process -Name $_ -ErrorAction SilentlyContinue | Stop-Process -Force
        }
        Start-Sleep -Milliseconds 500
    }

    Write-Host "Creating staging dirs in VM" -ForegroundColor Cyan
    Invoke-Command -Session $session -ScriptBlock {
        New-Item -ItemType Directory -Force -Path 'C:\ScamAlert\Broker'        | Out-Null
        New-Item -ItemType Directory -Force -Path 'C:\ScamAlert\DriverBridge'  | Out-Null
    }

    $brokerSize = [math]::Round((Get-ChildItem $brokerOut -Recurse | Measure-Object Length -Sum).Sum / 1MB, 1)
    $bridgeSize = [math]::Round((Get-ChildItem $bridgeOut -Recurse | Measure-Object Length -Sum).Sum / 1MB, 1)
    Write-Host "Copying Broker publish ($brokerSize MB)" -ForegroundColor Cyan
    Copy-Item -ToSession $session -Path "$brokerOut\*" -Destination 'C:\ScamAlert\Broker' -Recurse -Force
    Write-Host "Copying Bridge publish ($bridgeSize MB)" -ForegroundColor Cyan
    Copy-Item -ToSession $session -Path "$bridgeOut\*" -Destination 'C:\ScamAlert\DriverBridge' -Recurse -Force

    Write-Host "Starting services in VM" -ForegroundColor Cyan
    $startResult = Invoke-Command -Session $session -ScriptBlock {
        $logDir = 'C:\ScamAlert\logs'
        New-Item -ItemType Directory -Force -Path $logDir | Out-Null

        $brokerExe = 'C:\ScamAlert\Broker\ScamAlert.Broker.exe'
        $bridgeExe = 'C:\ScamAlert\DriverBridge\ScamAlert.DriverBridge.exe'

        $brokerProc = Start-Process -FilePath $brokerExe -PassThru -WindowStyle Hidden `
                                    -RedirectStandardOutput "$logDir\broker.out.log" `
                                    -RedirectStandardError  "$logDir\broker.err.log"

        Start-Sleep -Seconds 1   # give broker a moment to come up before bridge connects

        $bridgeProc = Start-Process -FilePath $bridgeExe -PassThru -WindowStyle Hidden `
                                    -RedirectStandardOutput "$logDir\bridge.out.log" `
                                    -RedirectStandardError  "$logDir\bridge.err.log"

        [pscustomobject]@{
            BrokerPid = $brokerProc.Id
            BridgePid = $bridgeProc.Id
            LogDir    = $logDir
            SignalFile = Join-Path $env:LOCALAPPDATA 'ScamAlert\signals.jsonl'
        }
    }

    $startResult | Format-List

    Write-Host ""
    Write-Host "Broker + Bridge are running inside ScamAlertDev." -ForegroundColor Green
    Write-Host "Generate inbound traffic from the host:" -ForegroundColor Yellow
    Write-Host "  Test-NetConnection -ComputerName <vm-ip> -Port 3389"
    Write-Host ""
    Write-Host "Tail the broker signal file from the host:" -ForegroundColor Yellow
    Write-Host "  Invoke-Command -VMName $VmName -Credential `$cred -ScriptBlock {"
    Write-Host "      Get-Content -Wait -Tail 0 -Path '$($startResult.SignalFile)'"
    Write-Host "  }"
}
finally {
    Remove-PSSession $session
}
