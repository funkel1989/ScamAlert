#include "Device.h"
#include "WfpMonitor.h"

static VOID ScamAlertUnload(_In_ PDRIVER_OBJECT DriverObject)
{
    UNREFERENCED_PARAMETER(DriverObject);
    ScamAlertStopWfpMonitor();
    ScamAlertDeleteDevice();
}

extern "C" NTSTATUS DriverEntry(
    _In_ PDRIVER_OBJECT DriverObject,
    _In_ PUNICODE_STRING RegistryPath)
{
    UNREFERENCED_PARAMETER(RegistryPath);

    DriverObject->DriverUnload = ScamAlertUnload;

    NTSTATUS status = ScamAlertCreateDevice(DriverObject);
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    status = ScamAlertStartWfpMonitor(ScamAlertGetDeviceObject());
    if (!NT_SUCCESS(status))
    {
        ScamAlertDeleteDevice();
        return status;
    }

    return STATUS_SUCCESS;
}
