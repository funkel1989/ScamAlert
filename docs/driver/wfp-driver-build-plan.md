# WFP Driver Build Plan (Execution Order)

This wraps the existing detailed plan at
[docs/superpowers/plans/2026-05-06-scamalert-wfp-monitor.md](../superpowers/plans/2026-05-06-scamalert-wfp-monitor.md)
with a current-state snapshot and a concrete execution order for the C++
driver work funkel1989 is now responsible for.

## Current State

- `.NET` side: `ScamAlert.Api`, `ScamAlert.Broker`, `ScamAlert.Core`,
  `ScamAlert.Data`, `ScamAlert.Tray`, `ScamAlert.Contracts` exist.
- `tools/ScamAlert.DriverSimulator` exists (the thing being replaced).
- `native/`, `scripts/driver/`, `src/ScamAlert.Broker.Client/`, and
  `src/ScamAlert.DriverBridge/` do **not** exist yet.
- WDK is **not** confirmed installed on the dev box. `fwpsk.h` /
  `fwpkclnt.lib` need to resolve before any C++ build will succeed.

## Two Milestones (Don't Skip A)

**Milestone A - Observe-Only.** The driver detects inbound TCP attempts on
3389 / 22 / 23, builds a `SCAMALERT_CONNECTION_EVENT`, queues it via IOCTL,
and **always returns `FWP_ACTION_PERMIT`**. The bridge forwards the event
to the broker over `scamalert-driver-events`. The broker's allow / block
decision rides back over the IOCTL contract but the driver does not
enforce it yet. This is the safe path that proves wiring without risking
BSOD-on-bad-pend in a VM.

**Milestone B - Pend-And-Decide Enforcement.** The driver pends initial
ALE authorization with `FwpsPendOperation0`, waits for the broker decision
(bounded), then completes with `FwpsCompleteOperation0`. This is what
"pause port activations" really requires. It has packet clone / reinject
nuances at `ALE_AUTH_RECV_ACCEPT` and is gated behind A passing in a VM.

The user's "detect and pause" wording maps to Milestone B, but the
existing plan (and `wfp-integration-contract.md`) explicitly requires A
first. We follow that staging.

## Prereqs Before Any Driver Code Compiles

1. WDK installed (matching the SDK already on disk).
2. A Windows VM with test-signing on (`bcdedit /set testsigning on`,
   reboot). Do not enable test-signing on the host workstation.
3. Visual Studio 2022 with the C++ desktop workload + Spectre-mitigated
   libs the WDK templates pull in.
4. Run `scripts/driver/check-driver-prereqs.ps1` (built in Task 0) -
   FwpskHeader / FwpkclntLibrary / TestSigning all green inside the VM.

## Execution Order (Bottom-Up, Compile-At-Each-Step)

The order below differs slightly from the source plan's numbering. It
front-loads the bits that compile without WDK so progress is verifiable
on the host machine, and isolates the kernel-only steps to the VM.

### Host-Buildable (No WDK Needed)

1. **Task 0 - Prereq script + WDK setup doc.**
   `scripts/driver/check-driver-prereqs.ps1`,
   `docs/driver/wdk-setup.md`. No code yet.
2. **Task 3 (header only) - Shared IOCTL header.**
   `native/ScamAlert.Driver.Shared/ScamAlertDriverIoctl.h`. Pure C, no
   WDK include path required at this point because nothing builds it yet.
   Pair with the C# `NativeDriverContracts.cs` and the
   `Marshal.SizeOf` layout test (sizes 120 / 20). Layout test is the
   binary-contract guard rail.
3. **Task 1 - `ScamAlert.Broker.Client` library.**
   Wraps the existing `scamalert-driver-events` newline-JSON pipe so the
   bridge and the simulator share the same transport.
4. **Task 2 - `ScamAlert.DriverBridge` worker shell.**
   Empty worker that we will give a device handle in Task 5.
5. **Task 5 - DriverBridge device IOCTL client.**
   `DeviceIoControl` against `\\.\ScamAlertWfp` for both
   `IOCTL_SCAMALERT_GET_EVENT` and `IOCTL_SCAMALERT_COMPLETE_EVENT`,
   marshalling to `NativeConnectionEvent` / `NativeConnectionDecision`.
   Can be unit-tested with a fake handle abstraction; full E2E waits
   for the driver.

### VM-Only (WDK Required)

6. **Task 4 - WDK driver skeleton.**
   `Driver.cpp` + `Device.cpp` + `Device.h` + INF + vcxproj. Just
   `IRP_MJ_CREATE` / `IRP_MJ_CLOSE`, `IoCreateDevice`, symbolic link.
   `sc create` and `\\.\ScamAlertWfp` should open from user mode.
7. **Task 6 - IOCTL event queue.**
   `EventQueue.h` / `EventQueue.cpp` (`LIST_ENTRY` + `KSPIN_LOCK`,
   `ExAllocatePool2` with `'aScS'` tag), wire `IRP_MJ_DEVICE_CONTROL`
   for the two IOCTLs. With the simulator pushing fake events into the
   queue we can prove the bridge-driver boundary before WFP enters the
   picture.
8. **Task 7 - Observe-only WFP registration.**
   `WfpMonitor.h` / `WfpMonitor.cpp`. Register IPv4 + IPv6 callouts at
   `FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V4 / V6` with the GUIDs from the
   constants block, filter local ports 3389 / 22 / 23, build the event
   and call `ScamAlertQueueConnectionEvent`, return
   `FWP_ACTION_PERMIT`. Reauthorize events are skipped.
9. **Task 8 - Install + VM validation.**
   `bcdedit /set testsigning on`, `pnputil /add-driver`, `sc start`,
   generate inbound RDP / SSH / Telnet, confirm the
   driver-event -> bridge -> broker -> tray -> JSONL signal chain.
10. **Task 9 - Enforcement gate doc.** Records the Milestone B
    requirements so the next plan can be written cleanly.

## Risks / Watch-Outs

- **IPv4 byte order.** `ScamAlertWriteIpv4Source` uses
  `RtlUlongByteSwap`. Task 8 validation must verify the printed IP is
  not reversed; if it is on this WDK / runtime, drop the swap and re-run.
- **Pool tag.** `'aScS'` is the agreed tag - keep it consistent so
  `!poolused` triage works.
- **Spinlock IRQL.** Allocation happens at `PASSIVE_LEVEL` before lock
  acquisition. Don't move `ExAllocatePool2` inside the lock.
- **Reauth filter.** The `FWP_CONDITION_FLAG_IS_REAUTHORIZE` check is
  load-bearing. Without it we will spam events on every keepalive.
- **`classifyOut->rights` check.** If `FWPS_RIGHT_ACTION_WRITE` is
  unset, return without touching `actionType`.
- **No cloud / UI in kernel.** All policy stays in the broker; the
  driver only queues events and (in Milestone B) pends operations.

## Open Decisions Before I Start Code

1. WDK installed and a test-signing VM ready, yes / no?
2. Stick to the Milestone-A-first staging (recommended) or take the
   risk of jumping straight to pend-and-decide?
3. Pool tag `'aScS'` and the device / DOS-link names from
   `## Constants And Contract Values` are locked - confirm.

Once those are answered I will start at Task 0 and work down the order
above, committing after each task as the source plan dictates.
