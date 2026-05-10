#include "Device.h"

static PDEVICE_OBJECT g_DeviceObject = nullptr;
static UNICODE_STRING g_DeviceName    = RTL_CONSTANT_STRING(L"\\Device\\ScamAlertWfp");
static UNICODE_STRING g_SymbolicLink  = RTL_CONSTANT_STRING(L"\\DosDevices\\ScamAlertWfp");

static NTSTATUS ScamAlertCreateClose(
    _In_ PDEVICE_OBJECT DeviceObject,
    _Inout_ PIRP Irp)
{
    UNREFERENCED_PARAMETER(DeviceObject);

    Irp->IoStatus.Status = STATUS_SUCCESS;
    Irp->IoStatus.Information = 0;
    IoCompleteRequest(Irp, IO_NO_INCREMENT);
    return STATUS_SUCCESS;
}

NTSTATUS ScamAlertCreateDevice(_In_ PDRIVER_OBJECT DriverObject)
{
    NTSTATUS status = IoCreateDevice(
        DriverObject,
        0,
        &g_DeviceName,
        FILE_DEVICE_UNKNOWN,
        FILE_DEVICE_SECURE_OPEN,
        FALSE,
        &g_DeviceObject);

    if (!NT_SUCCESS(status))
    {
        return status;
    }

    DriverObject->MajorFunction[IRP_MJ_CREATE] = ScamAlertCreateClose;
    DriverObject->MajorFunction[IRP_MJ_CLOSE]  = ScamAlertCreateClose;

    status = IoCreateSymbolicLink(&g_SymbolicLink, &g_DeviceName);
    if (!NT_SUCCESS(status))
    {
        IoDeleteDevice(g_DeviceObject);
        g_DeviceObject = nullptr;
        return status;
    }

    return STATUS_SUCCESS;
}

VOID ScamAlertDeleteDevice()
{
    IoDeleteSymbolicLink(&g_SymbolicLink);

    if (g_DeviceObject != nullptr)
    {
        IoDeleteDevice(g_DeviceObject);
        g_DeviceObject = nullptr;
    }
}
