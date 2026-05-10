# Builds ScamAlert.Tray (WinForms NotifyIcon + prompt dialog) self-contained
# for win-x64 and pushes it into the dev VM.
#
# The tray is a desktop UI app: it must run in an interactive logon session,
# not in PowerShell Direct's session 0. So this script copies the bits and
# wires an auto-start shortcut into the dev user's Startup folder. Bringing
# up the tray is then as simple as logging into the VM via vmconnect (or
# RDP) as that user.
#
# Run on the host. Prompts for the VM dev user credential.
#
# Usage:
#   scripts/driver/deploy-tray-to-vm.ps1
#   scripts/driver/deploy-tray-to-vm.ps1 -Configuration Debug -SkipBuild
#
# After this finishes:
#   1. Open Hyper-V Manager -> Connect to ScamAlertDev (or vmconnect.exe).
#   2. Log in as the dev user. The tray launches automatically; an icon
#      appears in the system tray.
#   3. From the host, trigger an inbound RDP attempt; the prompt dialog
#      pops in the VM. Click Allow or Block; the host-side TCP connect
#      reflects the choice.

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
$trayProj = Join-Path $repoRoot 'src\ScamAlert.Tray\ScamAlert.Tray.csproj'
$trayOut  = Join-Path $repoRoot 'build\publish\ScamAlert.Tray'

if (-not $SkipBuild) {
    Write-Host "Publishing ScamAlert.Tray (self-contained, win-x64) ..." -ForegroundColor Cyan
    dotnet publish $trayProj -c $Configuration -r win-x64 --self-contained true -o $trayOut --nologo /clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) { throw "Tray publish failed." }
}

if (-not (Test-Path $trayOut)) {
    throw "Tray publish output not found at $trayOut. Re-run without -SkipBuild."
}

$cred    = Get-Credential -UserName $UserName -Message "Local admin password for $VmName"
$session = New-PSSession -VMName $VmName -Credential $cred

try {
    Write-Host "Stopping any prior Tray process inside the VM" -ForegroundColor Cyan
    Invoke-Command -Session $session -ScriptBlock {
        Get-Process -Name 'ScamAlert.Tray' -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Milliseconds 500
    }

    Write-Host "Creating staging dir in VM" -ForegroundColor Cyan
    Invoke-Command -Session $session -ScriptBlock {
        New-Item -ItemType Directory -Force -Path 'C:\ScamAlert\Tray' | Out-Null
    }

    $traySize = [math]::Round((Get-ChildItem $trayOut -Recurse | Measure-Object Length -Sum).Sum / 1MB, 1)
    Write-Host "Copying Tray publish ($traySize MB)" -ForegroundColor Cyan
    Copy-Item -ToSession $session -Path "$trayOut\*" -Destination 'C:\ScamAlert\Tray' -Recurse -Force

    Write-Host "Wiring auto-start shortcut into the dev user's Startup folder" -ForegroundColor Cyan
    $startupResult = Invoke-Command -Session $session -ScriptBlock {
        $startupDir = [Environment]::GetFolderPath('Startup')
        New-Item -ItemType Directory -Force -Path $startupDir | Out-Null
        $shortcutPath = Join-Path $startupDir 'ScamAlert.Tray.lnk'

        $wsh   = New-Object -ComObject WScript.Shell
        $shortcut = $wsh.CreateShortcut($shortcutPath)
        $shortcut.TargetPath       = 'C:\ScamAlert\Tray\ScamAlert.Tray.exe'
        $shortcut.WorkingDirectory = 'C:\ScamAlert\Tray'
        $shortcut.WindowStyle      = 7   # minimized
        $shortcut.Description      = 'ScamAlert tray prompt UI'
        $shortcut.Save()

        [pscustomobject]@{
            ShortcutPath = $shortcutPath
            TargetExe    = 'C:\ScamAlert\Tray\ScamAlert.Tray.exe'
            User         = $env:USERNAME
        }
    }

    $startupResult | Format-List

    Write-Host ""
    Write-Host "Tray binaries staged at C:\ScamAlert\Tray inside $VmName." -ForegroundColor Green
    Write-Host "Next step (interactive UI required):" -ForegroundColor Yellow
    Write-Host "  1. vmconnect.exe localhost $VmName     # or open from Hyper-V Manager"
    Write-Host "  2. Log in as $UserName"
    Write-Host "  3. The tray icon appears in the system tray automatically (Startup shortcut)."
    Write-Host "  4. From the host, attempt inbound RDP/SSH/Telnet against the VM IP."
    Write-Host "     A prompt dialog will pop on the VM console; click Allow or Block."
    Write-Host ""
    Write-Host "If the tray didn't auto-start (e.g., already-active session), launch it manually inside the VM:" -ForegroundColor DarkGray
    Write-Host "  C:\ScamAlert\Tray\ScamAlert.Tray.exe"
}
finally {
    Remove-PSSession $session
}
