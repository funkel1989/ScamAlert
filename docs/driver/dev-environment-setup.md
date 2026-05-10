# ScamAlert WFP Driver - Dev Environment Setup

This is the end-to-end runbook for building the ScamAlert WFP driver on the host, pushing it into a Windows development VM, and validating that blocked or allowed traffic produces the expected ScamAlert signals.

The normal workflow is:

- Host machine: Visual Studio Build Tools, WDK, .NET SDK, repo checkout, and all build scripts.
- Development VM: Windows test target with test signing enabled, Secure Boot disabled, WinRM/PowerShell Direct access, and the ScamAlert driver/user-mode services copied from the host.

Do not install or test the kernel driver directly on the host. A driver bug can hang or crash Windows.

## Quick Flow

Run these from an elevated PowerShell prompt on the host unless a step explicitly says it runs inside the VM.

```powershell
scripts/driver/check-driver-prereqs.ps1
scripts/driver/create-dev-vm.ps1 -IsoPath "D:\HyperV\ISOs\Win11_25H2_EnglishInternational_x64.iso"
scripts/driver/finalize-dev-vm.ps1
# Copy configure-dev-vm-inside.ps1 into the VM, run it from elevated VM PowerShell, then let the VM reboot.
scripts/driver/verify-vm-access.ps1
scripts/driver/build-driver.ps1 -SkipRestore
scripts/driver/deploy-driver-to-vm.ps1
scripts/driver/prep-vm-for-traffic.ps1
scripts/driver/deploy-userland-to-vm.ps1
scripts/driver/deploy-tray-to-vm.ps1
```

After those steps, log into the VM interactively so the tray can start, generate test traffic to the VM IP printed by `prep-vm-for-traffic.ps1`, and check the broker signals file.

## 1. Validate Host Tooling

The host builds the native driver and managed projects. Validate the host before creating or deploying the VM:

```powershell
scripts/driver/check-driver-prereqs.ps1
```

If this fails because Visual Studio Build Tools or the WDK are missing, follow [WDK setup](wdk-setup.md), then rerun the prerequisite check. The native project consumes the WDK NuGet package declared in the `.vcxproj`, but the host still needs the Visual Studio C++/Windows build tools.

## 2. Create And Finalize The VM

Create the Hyper-V VM from a Windows evaluation ISO:

```powershell
scripts/driver/create-dev-vm.ps1 -IsoPath "D:\HyperV\ISOs\Win11_25H2_EnglishInternational_x64.iso"
```

Complete Windows setup in the VM console. The scripts assume the default VM name is `ScamAlertDev` and the default local user is `dev`, but both can be overridden through script parameters.

After Windows setup completes, run:

```powershell
scripts/driver/finalize-dev-vm.ps1
```

This detaches the install ISO, turns Secure Boot off, restarts the VM, and opens the VM console. Test signing and PowerShell remoting are enabled by the guest script in the next step.

## 3. Configure The Guest

Copy the guest setup script into the VM. If PowerShell Direct is available:

```powershell
$cred = Get-Credential -UserName dev
$session = New-PSSession -VMName ScamAlertDev -Credential $cred
Copy-Item -ToSession $session -Path scripts/driver/configure-dev-vm-inside.ps1 -Destination C:\Users\dev\Desktop\configure-dev-vm-inside.ps1
Remove-PSSession $session
```

Then open an elevated PowerShell prompt inside the VM and run:

```powershell
cd $env:USERPROFILE\Desktop
.\configure-dev-vm-inside.ps1
```

The guest script enables PowerShell remoting, configures the firewall rules needed for the test loop, and applies the VM-side settings used by the deploy scripts.

## 4. Verify VM Access

From the host, validate that the VM is reachable and configured for driver testing:

```powershell
scripts/driver/verify-vm-access.ps1
```

This checks the VM test-signing state, Secure Boot status, remoting access, and network profile. Fix any failures before deploying the driver.

## 5. Build And Deploy

Build the driver:

```powershell
scripts/driver/build-driver.ps1 -SkipRestore
```

Deploy the driver service to the VM:

```powershell
scripts/driver/deploy-driver-to-vm.ps1
```

The current development deploy copies the `.sys` file to the VM and installs it with the Service Control Manager. It does not use `pnputil` because this branch is using the raw development driver service path, not a signed installer package.

Prepare the VM listener and deploy user-mode processes:

```powershell
scripts/driver/prep-vm-for-traffic.ps1
scripts/driver/deploy-userland-to-vm.ps1
scripts/driver/deploy-tray-to-vm.ps1
```

`deploy-userland-to-vm.ps1` publishes and starts the broker and driver bridge under `C:\ScamAlert`. `deploy-tray-to-vm.ps1` publishes the tray app and creates a Startup shortcut. The tray still needs an interactive VM login.

## 6. Run A Traffic Test

Use the VM IP printed by `prep-vm-for-traffic.ps1`.

From the host:

```powershell
Test-NetConnection -ComputerName <vm-ip> -Port 3389
```

Inside the VM, or through PowerShell Direct, watch the broker signal log:

```powershell
$cred = Get-Credential -UserName dev
Invoke-Command -VMName ScamAlertDev -Credential $cred -ScriptBlock {
    Get-Content -Wait -Tail 0 -Path "$env:LOCALAPPDATA\ScamAlert\signals.jsonl"
}
```

Expected behavior:

- The WFP driver observes inbound RDP traffic and queues a binary event.
- `ScamAlert.DriverBridge` reads the event from `\\.\ScamAlertWfp`, converts it to the broker named-pipe JSON contract, and sends it to the broker.
- The broker writes a signal entry and waits for a tray decision.
- The bridge completes the kernel decision. If the bridge never completes the pending operation, the driver fails the stale pending operation closed after the kernel timeout.

## 7. Diagnostics

Driver counters can be checked while the bridge is running:

```powershell
$cred = Get-Credential -UserName dev
Invoke-Command -VMName ScamAlertDev -Credential $cred -FilePath scripts/driver/probe-driver-stats.ps1
```

The stats probe reads:

- `ClassifyEntered`
- `SelfInjectedSkipped`
- `EventsQueued`
- `PendOk`
- `AllowInjected`
- `BlockReleased`
- `TimedOutFailBlock`
- `EventsDropped`
- `PendingRejected`

Use the event probe only for low-level diagnostics:

```powershell
$cred = Get-Credential -UserName dev
Invoke-Command -VMName ScamAlertDev -Credential $cred -FilePath scripts/driver/probe-driver-events.ps1
```

`probe-driver-events.ps1` drains events from the driver queue and does not send completion decisions back to the kernel. Stop `ScamAlert.DriverBridge` before using it, or use it only when you intentionally want to inspect raw events without the normal bridge loop.

## Troubleshooting

Access denied opening `\\.\ScamAlertWfp` usually means the caller is not elevated or is not running as an administrator/service account. The device object is intentionally restricted to administrators and LocalSystem.

If the driver will not unload or redeploy cleanly, reboot the VM and rerun `deploy-driver-to-vm.ps1`. During active WFP testing, stale callout or service state can survive failed unload attempts.

If no prompt appears, confirm the broker and bridge are running under `C:\ScamAlert`, then log into the VM interactively so the tray Startup shortcut runs. The broker can record signals without the tray, but user decisions require the tray process.

If `EventsDropped` or `PendingRejected` increases, the bridge is not keeping up with kernel traffic or the event/pending queues are full. Reduce test traffic, restart the bridge, and inspect broker/bridge logs before continuing.
