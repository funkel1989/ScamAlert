#pragma once

#include <ntddk.h>

NTSTATUS ScamAlertStartWfpMonitor(_In_ PDEVICE_OBJECT DeviceObject);
VOID     ScamAlertStopWfpMonitor();

// Injection handle used to (a) reinject permitted inbound clones via
// FwpsInjectTransportReceiveAsync0 and (b) recognize self-injected packets
// in classifyFn via FwpsQueryPacketInjectionState0. NULL if WFP startup
// failed; PendingOps treats that as fatal-for-this-op.
HANDLE   ScamAlertGetInjectionHandle();

// Fills a 9-element LONG64 array with the diagnostic counters defined in
// WfpMonitor.cpp. Caller allocates the array; we don't take a
// SCAMALERT_DRIVER_STATS* directly to avoid a circular header dependency.
VOID ScamAlertGetMonitorCounters(_Out_writes_(9) LONG64* out);

// Counter bumpers called from PendingOps.cpp and classify resource-limit paths.
VOID ScamAlertBumpAllowInjected();
VOID ScamAlertBumpBlockReleased();
VOID ScamAlertBumpTimedOutFailBlock();
VOID ScamAlertBumpEventsDropped();
VOID ScamAlertBumpPendingRejected();
