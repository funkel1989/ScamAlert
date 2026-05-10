# WFP Driver Current State And Maintenance Notes

The original implementation plan is still available at
[docs/superpowers/plans/2026-05-06-scamalert-wfp-monitor.md](../superpowers/plans/2026-05-06-scamalert-wfp-monitor.md).
This file reflects the current branch state and the practical maintenance checklist for the native WFP path.

## Current State

- The managed application projects already exist: `ScamAlert.Api`, `ScamAlert.Broker`, `ScamAlert.Core`, `ScamAlert.Data`, `ScamAlert.Tray`, and `ScamAlert.Contracts`.
- The legacy simulator remains at `tools/ScamAlert.DriverSimulator` for local broker/tray testing without a kernel driver.
- The native driver lives under `native/ScamAlert.WfpDriver`.
- Shared driver IOCTL contracts live under `native/ScamAlert.Driver.Shared`.
- The user-mode driver bridge lives under `src/ScamAlert.DriverBridge`.
- Host/VM helper scripts live under `scripts/driver`.

The current driver observes inbound protected-port attempts at:

- `FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V4`
- `FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V6`

The protected ports are:

- `3389` for RDP
- `22` for SSH
- `23` for Telnet

For matching traffic, the driver queues a connection event, pends the WFP authorization, and waits for `ScamAlert.DriverBridge` to complete the allow/block decision. If the pending operation becomes stale, the kernel timeout fails it closed.

## Runtime Flow

1. The WFP callout sees an inbound protected-port authorization.
2. The driver creates a bounded kernel event and a bounded pending-operation entry.
3. `ScamAlert.DriverBridge` reads the event from `\\.\ScamAlertWfp`.
4. DriverBridge sends a newline-delimited JSON request to `ScamAlert.Broker` on the `scamalert-driver-events` named pipe.
5. The broker applies remembered rules or prompts through the tray.
6. DriverBridge writes the allow/block decision back through `IOCTL_SCAMALERT_COMPLETE_EVENT`.
7. The driver completes the pending WFP operation.

The driver device is created with a security descriptor that restricts access to LocalSystem and administrators.

## Binary Contract Guard Rails

The native IOCTL contract is defined in:

- `native/ScamAlert.Driver.Shared/ScamAlertDriverIoctl.h`

The managed mirror is defined in:

- `src/ScamAlert.DriverBridge/Driver/NativeDriverContracts.cs`

The current structure sizes are:

- `SCAMALERT_CONNECTION_EVENT`: 122 bytes
- `SCAMALERT_CONNECTION_DECISION`: 20 bytes
- `SCAMALERT_DRIVER_STATS`: 72 bytes

These sizes are guarded by `NativeDriverContractsTests`. Any native layout change must update the managed mirror and tests in the same change.

## Build And Deploy

Host prerequisite validation:

```powershell
scripts/driver/check-driver-prereqs.ps1
```

Driver build:

```powershell
scripts/driver/build-driver.ps1 -SkipRestore
```

Development VM deploy:

```powershell
scripts/driver/deploy-driver-to-vm.ps1
scripts/driver/prep-vm-for-traffic.ps1
scripts/driver/deploy-userland-to-vm.ps1
scripts/driver/deploy-tray-to-vm.ps1
```

The current development deploy uses the Service Control Manager to install and start the copied `.sys` file in the VM. It is not a signed package install flow.

See [dev environment setup](dev-environment-setup.md) for the full host/VM runbook and [WDK setup](wdk-setup.md) for tooling requirements.

## Verification Checklist

Before treating the WFP path as healthy:

```powershell
scripts/driver/build-driver.ps1 -SkipRestore
dotnet test ScamAlert.sln --no-restore /p:UseSharedCompilation=false
scripts/driver/verify-vm-access.ps1
$cred = Get-Credential -UserName dev
Invoke-Command -VMName ScamAlertDev -Credential $cred -FilePath scripts/driver/probe-driver-stats.ps1
```

For an end-to-end traffic check, log into the VM interactively so the tray runs, generate traffic to the VM IP, and confirm `%LOCALAPPDATA%\ScamAlert\signals.jsonl` records the expected signal flow.

## Current Watch-Outs

- WFP classify callbacks have strict IRQL and memory constraints. Keep blocking work in user mode and keep kernel allocations bounded.
- The event probe drains driver events and does not complete decisions. Use it only when the bridge is stopped or when intentionally inspecting raw driver output.
- `EventsDropped`, `PendingRejected`, and `TimedOutFailBlock` are operational warning counters. Increasing values mean the bridge is falling behind, the queues are too small for the traffic pattern, or decisions are not returning.
- If the driver will not unload or redeploy cleanly during testing, reboot the VM and rerun the deploy script.
- The tray must run in an interactive user session. Broker and bridge processes can run without the tray, but user prompts cannot.

## Future Work

- Replace the raw development service install with a signed driver package and installer flow.
- Add WinDbg/kernel crash-dump setup to the VM runbook.
- Make the bridge/driver fail policy configurable after the MVP behavior is finalized.
- Expand protected services beyond the current RDP/SSH/Telnet set.
- Add stress tests around queue pressure, timeout behavior, and repeated driver reloads.
