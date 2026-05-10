#pragma once

#include <ntddk.h>

NTSTATUS ScamAlertStartWfpMonitor(_In_ PDEVICE_OBJECT DeviceObject);
VOID     ScamAlertStopWfpMonitor();

// Fills a 7-element LONG64 array with the diagnostic counters defined
// in WfpMonitor.cpp. Caller must allocate the array; we don't take a
// SCAMALERT_DRIVER_STATS* directly to avoid a header dependency in the
// other direction.
VOID ScamAlertGetMonitorCounters(_Out_writes_(7) LONG64* out);
