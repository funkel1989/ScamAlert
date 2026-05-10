#pragma once

#include <ntddk.h>

NTSTATUS ScamAlertStartWfpMonitor(_In_ PDEVICE_OBJECT DeviceObject);
VOID     ScamAlertStopWfpMonitor();
