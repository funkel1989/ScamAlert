#include "Device.h"
#include "EventQueue.h"
#include "PendingOps.h"
#include "WfpMonitor.h"

#include <wdmsec.h>

static PDEVICE_OBJECT g_DeviceObject = nullptr;
static UNICODE_STRING g_DeviceName    = RTL_CONSTANT_STRING(L"\\Device\\ScamAlertWfp");
static UNICODE_STRING g_SymbolicLink  = RTL_CONSTANT_STRING(L"\\DosDevices\\ScamAlertWfp");
static UNICODE_STRING g_DeviceSddl    = RTL_CONSTANT_STRING(L"D:P(A;;GA;;;SY)(A;;GA;;;BA)");
static BOOLEAN        g_QueueInitialized = FALSE;
static const GUID     g_DeviceClassGuid =
    { 0x3f01cb56, 0xa822, 0x44d7, { 0x94, 0x7e, 0x5e, 0x12, 0x76, 0xbb, 0x40, 0xd9 } };

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

static NTSTATUS ScamAlertDeviceControl(
    _In_ PDEVICE_OBJECT DeviceObject,
    _Inout_ PIRP Irp)
{
    UNREFERENCED_PARAMETER(DeviceObject);

    PIO_STACK_LOCATION stack = IoGetCurrentIrpStackLocation(Irp);
    const ULONG code         = stack->Parameters.DeviceIoControl.IoControlCode;
    const ULONG inputLength  = stack->Parameters.DeviceIoControl.InputBufferLength;
    const ULONG outputLength = stack->Parameters.DeviceIoControl.OutputBufferLength;
    PVOID buffer             = Irp->AssociatedIrp.SystemBuffer;

    NTSTATUS  status      = STATUS_INVALID_DEVICE_REQUEST;
    ULONG_PTR information = 0;

    if (code == IOCTL_SCAMALERT_GET_EVENT)
    {
        if (outputLength < sizeof(SCAMALERT_CONNECTION_EVENT))
        {
            status = STATUS_BUFFER_TOO_SMALL;
        }
        else
        {
            status = ScamAlertPopConnectionEvent(static_cast<SCAMALERT_CONNECTION_EVENT*>(buffer));
            if (NT_SUCCESS(status))
            {
                information = sizeof(SCAMALERT_CONNECTION_EVENT);
            }
        }
    }
    else if (code == IOCTL_SCAMALERT_COMPLETE_EVENT)
    {
        if (inputLength < sizeof(SCAMALERT_CONNECTION_DECISION))
        {
            status = STATUS_BUFFER_TOO_SMALL;
        }
        else
        {
            status = ScamAlertCompleteConnectionEvent(static_cast<SCAMALERT_CONNECTION_DECISION*>(buffer));
        }
    }
    else if (code == IOCTL_SCAMALERT_GET_STATS)
    {
        if (outputLength < sizeof(SCAMALERT_DRIVER_STATS))
        {
            status = STATUS_BUFFER_TOO_SMALL;
        }
        else
        {
            LONG64 counters[9] = {};
            ScamAlertGetMonitorCounters(counters);

            SCAMALERT_DRIVER_STATS* stats = static_cast<SCAMALERT_DRIVER_STATS*>(buffer);
            stats->ClassifyEntered     = static_cast<UINT64>(counters[0]);
            stats->SelfInjectedSkipped = static_cast<UINT64>(counters[1]);
            stats->EventsQueued        = static_cast<UINT64>(counters[2]);
            stats->PendOk              = static_cast<UINT64>(counters[3]);
            stats->AllowInjected       = static_cast<UINT64>(counters[4]);
            stats->BlockReleased       = static_cast<UINT64>(counters[5]);
            stats->TimedOutFailBlock   = static_cast<UINT64>(counters[6]);
            stats->EventsDropped       = static_cast<UINT64>(counters[7]);
            stats->PendingRejected     = static_cast<UINT64>(counters[8]);

            status = STATUS_SUCCESS;
            information = sizeof(SCAMALERT_DRIVER_STATS);
        }
    }

    Irp->IoStatus.Status = status;
    Irp->IoStatus.Information = information;
    IoCompleteRequest(Irp, IO_NO_INCREMENT);
    return status;
}

NTSTATUS ScamAlertCreateDevice(_In_ PDRIVER_OBJECT DriverObject)
{
    NTSTATUS status = ScamAlertInitializeEventQueue();
    if (!NT_SUCCESS(status))
    {
        return status;
    }
    g_QueueInitialized = TRUE;

    status = IoCreateDeviceSecure(
        DriverObject,
        0,
        &g_DeviceName,
        FILE_DEVICE_UNKNOWN,
        FILE_DEVICE_SECURE_OPEN,
        FALSE,
        &g_DeviceSddl,
        &g_DeviceClassGuid,
        &g_DeviceObject);

    if (!NT_SUCCESS(status))
    {
        ScamAlertDestroyEventQueue();
        g_QueueInitialized = FALSE;
        return status;
    }

    DriverObject->MajorFunction[IRP_MJ_CREATE]         = ScamAlertCreateClose;
    DriverObject->MajorFunction[IRP_MJ_CLOSE]          = ScamAlertCreateClose;
    DriverObject->MajorFunction[IRP_MJ_DEVICE_CONTROL] = ScamAlertDeviceControl;

    status = IoCreateSymbolicLink(&g_SymbolicLink, &g_DeviceName);
    if (!NT_SUCCESS(status))
    {
        IoDeleteDevice(g_DeviceObject);
        g_DeviceObject = nullptr;
        ScamAlertDestroyEventQueue();
        g_QueueInitialized = FALSE;
        return status;
    }

    // Pending-ops needs a valid g_DeviceObject for IoAllocateWorkItem,
    // so initialize after IoCreateDevice has succeeded.
    status = ScamAlertInitializePendingOps();
    if (!NT_SUCCESS(status))
    {
        IoDeleteSymbolicLink(&g_SymbolicLink);
        IoDeleteDevice(g_DeviceObject);
        g_DeviceObject = nullptr;
        ScamAlertDestroyEventQueue();
        g_QueueInitialized = FALSE;
        return status;
    }

    return STATUS_SUCCESS;
}

PDEVICE_OBJECT ScamAlertGetDeviceObject()
{
    return g_DeviceObject;
}

VOID ScamAlertDeleteDevice()
{
    // Drain pending classifies first so no callouts hold open handles
    // when we tear the device down. PendingOps fail-blocks any survivors.
    ScamAlertDestroyPendingOps();

    IoDeleteSymbolicLink(&g_SymbolicLink);

    if (g_DeviceObject != nullptr)
    {
        IoDeleteDevice(g_DeviceObject);
        g_DeviceObject = nullptr;
    }

    if (g_QueueInitialized)
    {
        ScamAlertDestroyEventQueue();
        g_QueueInitialized = FALSE;
    }
}
