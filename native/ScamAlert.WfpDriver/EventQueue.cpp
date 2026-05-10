#include "EventQueue.h"
#include "PendingOps.h"

static LIST_ENTRY g_EventQueue;
static KSPIN_LOCK g_EventQueueLock;
static BOOLEAN    g_EventQueueInitialized = FALSE;
static LONG       g_EventQueueLength      = 0;
static constexpr LONG ScamAlertMaxQueuedEvents = 256;

NTSTATUS ScamAlertInitializeEventQueue()
{
    InitializeListHead(&g_EventQueue);
    KeInitializeSpinLock(&g_EventQueueLock);
    g_EventQueueLength = 0;
    g_EventQueueInitialized = TRUE;
    return STATUS_SUCCESS;
}

VOID ScamAlertDestroyEventQueue()
{
    if (!g_EventQueueInitialized)
    {
        return;
    }

    KIRQL oldIrql;
    KeAcquireSpinLock(&g_EventQueueLock, &oldIrql);

    while (!IsListEmpty(&g_EventQueue))
    {
        PLIST_ENTRY entry = RemoveHeadList(&g_EventQueue);
        SCAMALERT_EVENT_NODE* node = CONTAINING_RECORD(entry, SCAMALERT_EVENT_NODE, Link);
        if (g_EventQueueLength > 0)
        {
            --g_EventQueueLength;
        }
        ExFreePoolWithTag(node, SCAMALERT_POOL_TAG);
    }

    KeReleaseSpinLock(&g_EventQueueLock, oldIrql);
    g_EventQueueLength = 0;
    g_EventQueueInitialized = FALSE;
}

NTSTATUS ScamAlertQueueConnectionEvent(_In_ const SCAMALERT_CONNECTION_EVENT* Event)
{
    if (Event == nullptr)
    {
        return STATUS_INVALID_PARAMETER;
    }

    SCAMALERT_EVENT_NODE* node = static_cast<SCAMALERT_EVENT_NODE*>(
        ExAllocatePool2(POOL_FLAG_NON_PAGED, sizeof(SCAMALERT_EVENT_NODE), SCAMALERT_POOL_TAG));

    if (node == nullptr)
    {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    RtlCopyMemory(&node->Event, Event, sizeof(SCAMALERT_CONNECTION_EVENT));

    KIRQL oldIrql;
    KeAcquireSpinLock(&g_EventQueueLock, &oldIrql);

    if (g_EventQueueLength >= ScamAlertMaxQueuedEvents)
    {
        KeReleaseSpinLock(&g_EventQueueLock, oldIrql);
        ExFreePoolWithTag(node, SCAMALERT_POOL_TAG);
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    InsertTailList(&g_EventQueue, &node->Link);
    ++g_EventQueueLength;
    KeReleaseSpinLock(&g_EventQueueLock, oldIrql);

    return STATUS_SUCCESS;
}

NTSTATUS ScamAlertPopConnectionEvent(_Out_ SCAMALERT_CONNECTION_EVENT* Event)
{
    if (Event == nullptr)
    {
        return STATUS_INVALID_PARAMETER;
    }

    KIRQL oldIrql;
    KeAcquireSpinLock(&g_EventQueueLock, &oldIrql);

    if (IsListEmpty(&g_EventQueue))
    {
        KeReleaseSpinLock(&g_EventQueueLock, oldIrql);
        return STATUS_NO_MORE_ENTRIES;
    }

    PLIST_ENTRY entry = RemoveHeadList(&g_EventQueue);
    if (g_EventQueueLength > 0)
    {
        --g_EventQueueLength;
    }
    KeReleaseSpinLock(&g_EventQueueLock, oldIrql);

    SCAMALERT_EVENT_NODE* node = CONTAINING_RECORD(entry, SCAMALERT_EVENT_NODE, Link);
    RtlCopyMemory(Event, &node->Event, sizeof(SCAMALERT_CONNECTION_EVENT));
    ExFreePoolWithTag(node, SCAMALERT_POOL_TAG);

    return STATUS_SUCCESS;
}

NTSTATUS ScamAlertCompleteConnectionEvent(_In_ const SCAMALERT_CONNECTION_DECISION* Decision)
{
    if (Decision == nullptr) return STATUS_INVALID_PARAMETER;

    const SCAMALERT_DRIVER_DECISION verdict =
        static_cast<SCAMALERT_DRIVER_DECISION>(Decision->Decision);

    if (verdict != ScamAlertDecisionAllow && verdict != ScamAlertDecisionBlock)
    {
        return STATUS_INVALID_PARAMETER;
    }

    return ScamAlertCompletePendingOp(Decision->EventId, verdict);
}
