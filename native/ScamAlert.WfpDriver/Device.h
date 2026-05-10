#pragma once

#include <ntddk.h>

extern "C" DRIVER_INITIALIZE DriverEntry;

NTSTATUS ScamAlertCreateDevice(_In_ PDRIVER_OBJECT DriverObject);
VOID ScamAlertDeleteDevice();
