#include "PendingOps.h"

#include <fwpsk.h>
#include <fwpmk.h>

#include "Device.h"     // for ScamAlertGetDeviceObject
#include "EventQueue.h" // for SCAMALERT_POOL_TAG

// Wall-clock ceiling on how long a classify can stay pending before the
// kernel timeout DPC completes it with a fail-open PERMIT. The bridge's
// own broker request timeout is 30s, so we give the user-mode chain a
// generous window before stepping in. 100-ns ticks: 30s * 10^7.
constexpr LONGLONG ScamAlertPendingTimeoutTicks = 30LL * 10000000LL;

// Period at which the timeout DPC scans the pending table.
constexpr LONGLONG ScamAlertPendingScanPeriodTicks = 1LL * 10000000LL; // 1 second

static LIST_ENTRY  g_PendingList;
static KSPIN_LOCK  g_PendingLock;
static BOOLEAN     g_PendingInitialized = FALSE;

static KTIMER      g_TimeoutTimer;
static KDPC        g_TimeoutDpc;
static PIO_WORKITEM g_TimeoutWorkItem = nullptr;
static PDEVICE_OBJECT g_PendingDeviceObject = nullptr;

// The work item completes pending classify handles at PASSIVE_LEVEL,
// which Fwps* requires. We collect the to-be-completed handles in a
// per-tick local list under the spinlock, then drop the lock and run
// the completions.
typedef struct SCAMALERT_TIMEOUT_BATCH
{
    LIST_ENTRY            Entry;
    UINT64                ClassifyHandle;
} SCAMALERT_TIMEOUT_BATCH;

static VOID ScamAlertCompleteHandleAtPassive(UINT64 classifyHandle, SCAMALERT_DRIVER_DECISION decision);

static VOID NTAPI ScamAlertTimeoutWorkRoutine(
    _In_ PDEVICE_OBJECT DeviceObject,
    _In_opt_ PVOID Context)
{
    UNREFERENCED_PARAMETER(DeviceObject);

    LIST_ENTRY* batchHead = static_cast<LIST_ENTRY*>(Context);
    if (batchHead == nullptr) return;

    while (!IsListEmpty(batchHead))
    {
        PLIST_ENTRY entry = RemoveHeadList(batchHead);
        SCAMALERT_TIMEOUT_BATCH* item = CONTAINING_RECORD(entry, SCAMALERT_TIMEOUT_BATCH, Entry);

        ScamAlertCompleteHandleAtPassive(item->ClassifyHandle, ScamAlertDecisionAllow);
        ExFreePoolWithTag(item, SCAMALERT_POOL_TAG);
    }

    ExFreePoolWithTag(batchHead, SCAMALERT_POOL_TAG);
}

static VOID NTAPI ScamAlertTimeoutDpcRoutine(
    _In_ PKDPC Dpc,
    _In_opt_ PVOID DeferredContext,
    _In_opt_ PVOID SystemArgument1,
    _In_opt_ PVOID SystemArgument2)
{
    UNREFERENCED_PARAMETER(Dpc);
    UNREFERENCED_PARAMETER(DeferredContext);
    UNREFERENCED_PARAMETER(SystemArgument1);
    UNREFERENCED_PARAMETER(SystemArgument2);

    if (!g_PendingInitialized) return;

    LARGE_INTEGER now;
    KeQueryTickCount(&now);
    LONGLONG cutoffTicks = KeQueryTimeIncrement();
    LONGLONG nowIn100ns = now.QuadPart * cutoffTicks;

    // Gather expired entries under the spinlock, complete outside it
    LIST_ENTRY* batchHead = static_cast<LIST_ENTRY*>(
        ExAllocatePool2(POOL_FLAG_NON_PAGED, sizeof(LIST_ENTRY), SCAMALERT_POOL_TAG));
    if (batchHead == nullptr) return;
    InitializeListHead(batchHead);
    BOOLEAN any = FALSE;

    KIRQL oldIrql;
    KeAcquireSpinLock(&g_PendingLock, &oldIrql);

    PLIST_ENTRY entry = g_PendingList.Flink;
    while (entry != &g_PendingList)
    {
        PLIST_ENTRY next = entry->Flink;
        SCAMALERT_PENDING_NODE* node = CONTAINING_RECORD(entry, SCAMALERT_PENDING_NODE, Link);

        if (nowIn100ns - node->PendedAtTicks.QuadPart > ScamAlertPendingTimeoutTicks)
        {
            RemoveEntryList(&node->Link);

            SCAMALERT_TIMEOUT_BATCH* item = static_cast<SCAMALERT_TIMEOUT_BATCH*>(
                ExAllocatePool2(POOL_FLAG_NON_PAGED, sizeof(SCAMALERT_TIMEOUT_BATCH), SCAMALERT_POOL_TAG));
            if (item != nullptr)
            {
                item->ClassifyHandle = node->ClassifyHandle;
                InsertTailList(batchHead, &item->Entry);
                any = TRUE;
            }

            ExFreePoolWithTag(node, SCAMALERT_POOL_TAG);
        }

        entry = next;
    }

    KeReleaseSpinLock(&g_PendingLock, oldIrql);

    if (any && g_TimeoutWorkItem != nullptr)
    {
        IoQueueWorkItem(g_TimeoutWorkItem, ScamAlertTimeoutWorkRoutine, DelayedWorkQueue, batchHead);
    }
    else
    {
        ExFreePoolWithTag(batchHead, SCAMALERT_POOL_TAG);
    }
}

NTSTATUS ScamAlertInitializePendingOps()
{
    if (g_PendingInitialized) return STATUS_SUCCESS;

    InitializeListHead(&g_PendingList);
    KeInitializeSpinLock(&g_PendingLock);

    g_PendingDeviceObject = ScamAlertGetDeviceObject();
    if (g_PendingDeviceObject != nullptr)
    {
        g_TimeoutWorkItem = IoAllocateWorkItem(g_PendingDeviceObject);
    }

    KeInitializeTimer(&g_TimeoutTimer);
    KeInitializeDpc(&g_TimeoutDpc, ScamAlertTimeoutDpcRoutine, nullptr);

    LARGE_INTEGER dueTime;
    dueTime.QuadPart = -ScamAlertPendingScanPeriodTicks;
    KeSetTimerEx(&g_TimeoutTimer, dueTime, 1000 /* ms repeating */, &g_TimeoutDpc);

    g_PendingInitialized = TRUE;
    return STATUS_SUCCESS;
}

VOID ScamAlertDestroyPendingOps()
{
    if (!g_PendingInitialized) return;

    KeCancelTimer(&g_TimeoutTimer);

    if (g_TimeoutWorkItem != nullptr)
    {
        IoFreeWorkItem(g_TimeoutWorkItem);
        g_TimeoutWorkItem = nullptr;
    }

    // Drain any remaining pending classifies and fail-open them.
    LIST_ENTRY drainList;
    InitializeListHead(&drainList);

    KIRQL oldIrql;
    KeAcquireSpinLock(&g_PendingLock, &oldIrql);
    while (!IsListEmpty(&g_PendingList))
    {
        PLIST_ENTRY entry = RemoveHeadList(&g_PendingList);
        InsertTailList(&drainList, entry);
    }
    KeReleaseSpinLock(&g_PendingLock, oldIrql);

    while (!IsListEmpty(&drainList))
    {
        PLIST_ENTRY entry = RemoveHeadList(&drainList);
        SCAMALERT_PENDING_NODE* node = CONTAINING_RECORD(entry, SCAMALERT_PENDING_NODE, Link);
        ScamAlertCompleteHandleAtPassive(node->ClassifyHandle, ScamAlertDecisionAllow);
        ExFreePoolWithTag(node, SCAMALERT_POOL_TAG);
    }

    g_PendingInitialized = FALSE;
}

NTSTATUS ScamAlertAddPendingOp(
    _In_ const UINT8* EventId,
    _In_ UINT64       ClassifyHandle,
    _In_ UINT16       LayerId)
{
    if (EventId == nullptr || ClassifyHandle == 0)
    {
        return STATUS_INVALID_PARAMETER;
    }

    SCAMALERT_PENDING_NODE* node = static_cast<SCAMALERT_PENDING_NODE*>(
        ExAllocatePool2(POOL_FLAG_NON_PAGED, sizeof(SCAMALERT_PENDING_NODE), SCAMALERT_POOL_TAG));
    if (node == nullptr)
    {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    RtlCopyMemory(node->EventId, EventId, 16);
    node->ClassifyHandle = ClassifyHandle;
    node->LayerId        = LayerId;

    LARGE_INTEGER ticks;
    KeQueryTickCount(&ticks);
    node->PendedAtTicks.QuadPart = ticks.QuadPart * KeQueryTimeIncrement();

    KIRQL oldIrql;
    KeAcquireSpinLock(&g_PendingLock, &oldIrql);
    InsertTailList(&g_PendingList, &node->Link);
    KeReleaseSpinLock(&g_PendingLock, oldIrql);

    return STATUS_SUCCESS;
}

static VOID ScamAlertCompleteHandleAtPassive(UINT64 classifyHandle, SCAMALERT_DRIVER_DECISION decision)
{
    if (classifyHandle == 0) return;

    FWPS_CLASSIFY_OUT0 out = {};
    out.actionType = (decision == ScamAlertDecisionBlock)
        ? FWP_ACTION_BLOCK
        : FWP_ACTION_PERMIT;
    out.rights = FWPS_RIGHT_ACTION_WRITE;

    FwpsCompleteClassify0(classifyHandle, 0, &out);
    FwpsReleaseClassifyHandle0(classifyHandle);
}

NTSTATUS ScamAlertCompletePendingOp(
    _In_ const UINT8*               EventId,
    _In_ SCAMALERT_DRIVER_DECISION  Decision)
{
    if (EventId == nullptr) return STATUS_INVALID_PARAMETER;

    UINT64 handle = 0;

    KIRQL oldIrql;
    KeAcquireSpinLock(&g_PendingLock, &oldIrql);

    PLIST_ENTRY entry = g_PendingList.Flink;
    while (entry != &g_PendingList)
    {
        SCAMALERT_PENDING_NODE* node = CONTAINING_RECORD(entry, SCAMALERT_PENDING_NODE, Link);
        if (RtlEqualMemory(node->EventId, EventId, 16))
        {
            RemoveEntryList(&node->Link);
            handle = node->ClassifyHandle;
            ExFreePoolWithTag(node, SCAMALERT_POOL_TAG);
            break;
        }
        entry = entry->Flink;
    }

    KeReleaseSpinLock(&g_PendingLock, oldIrql);

    if (handle == 0)
    {
        // Already completed (timeout) - that's fine, ignore.
        return STATUS_SUCCESS;
    }

    if (KeGetCurrentIrql() == PASSIVE_LEVEL)
    {
        ScamAlertCompleteHandleAtPassive(handle, Decision);
        return STATUS_SUCCESS;
    }

    // We're not at PASSIVE_LEVEL - shouldn't happen for IOCTL paths, but
    // if it does, defer via a work item.
    return STATUS_INVALID_DEVICE_STATE;
}
