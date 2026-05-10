#include "PendingOps.h"

#include <fwpsk.h>
#include <fwpmk.h>

#include "Device.h"     // ScamAlertGetDeviceObject
#include "EventQueue.h" // SCAMALERT_POOL_TAG
#include "WfpMonitor.h" // ScamAlertGetInjectionHandle, counter bumpers

// Wall-clock ceiling on how long a classify can stay pending before the
// kernel timeout DPC fail-BLOCKS the connection. We deliberately fail-block
// rather than fail-open here because reinjecting a clone whose underlying
// connection has already TCP-timed-out is racy: NDIS state on the original
// NBL can be partially torn down by the time we try to clone it, which
// surfaced as IRQL_NOT_LESS_OR_EQUAL crashes during testing. The only place
// we clone+reinject is the IOCTL path, where the broker is alive and the
// connection is still fresh.
//
// 60s gives a human plenty of time to read and click Allow/Block in the
// tray prompt (broker's PromptTimeoutSeconds default is 30s, so user mode
// always responds first under normal conditions). Inbound TCP still gives
// up on its own around 21s, so a slow user past that point will see their
// connection retry-fail; the broker's verdict, when it eventually arrives,
// runs against an empty pending table and is silently ignored.
constexpr LONGLONG ScamAlertPendingTimeoutTicks    = 60LL * 10000000LL;
constexpr LONGLONG ScamAlertPendingScanPeriodTicks =  1LL * 10000000LL;

static LIST_ENTRY     g_PendingList;
static KSPIN_LOCK     g_PendingLock;
static BOOLEAN        g_PendingInitialized   = FALSE;

static KTIMER         g_TimeoutTimer;
static KDPC           g_TimeoutDpc;
static PIO_WORKITEM   g_TimeoutWorkItem      = nullptr;
static PDEVICE_OBJECT g_PendingDeviceObject  = nullptr;

// Forward decls for paths invoked from both classify and timeout flows.
static VOID ScamAlertReleasePendingNode(_In_ SCAMALERT_PENDING_NODE* node);
static NTSTATUS ScamAlertAllowReinjectInbound(_Inout_ SCAMALERT_PENDING_NODE* node);
static VOID ScamAlertBlockAndRelease(_In_ SCAMALERT_PENDING_NODE* node);

// Inject completion routine. Frees the cloned NBL and the pending node.
// Called by WFP at <= DISPATCH_LEVEL once the reinjected packet has been
// indicated to the stack.
static VOID NTAPI ScamAlertInjectComplete(
    _Inout_ void*            context,
    _Inout_ NET_BUFFER_LIST* netBufferList,
    _In_    BOOLEAN          dispatchLevel)
{
    UNREFERENCED_PARAMETER(dispatchLevel);

    SCAMALERT_PENDING_NODE* node = static_cast<SCAMALERT_PENDING_NODE*>(context);

    if (netBufferList != nullptr)
    {
        FwpsFreeCloneNetBufferList0(netBufferList, 0);
    }
    ScamAlertReleasePendingNode(node);
}

// Releases the saved NBL reference and frees the node's pool memory. Does
// NOT call FwpsCompleteOperation0; the caller must have already disposed of
// the completion context.
static VOID ScamAlertReleasePendingNode(_In_ SCAMALERT_PENDING_NODE* node)
{
    if (node == nullptr) return;

    if (node->NetBufferList != nullptr)
    {
        FwpsDereferenceNetBufferList0(node->NetBufferList, FALSE);
        node->NetBufferList = nullptr;
    }
    ExFreePoolWithTag(node, SCAMALERT_POOL_TAG);
}

// Container handed to the timeout work routine: an array of nodes that
// have been removed from the global list and are ready to be fail-opened.
typedef struct SCAMALERT_TIMEOUT_BATCH
{
    LIST_ENTRY Entry;
    SCAMALERT_PENDING_NODE* Node;
} SCAMALERT_TIMEOUT_BATCH;

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
        SCAMALERT_PENDING_NODE* node = item->Node;
        ExFreePoolWithTag(item, SCAMALERT_POOL_TAG);

        if (node == nullptr) continue;

        ScamAlertBumpTimedOutFailOpen();

        // Fail-BLOCK: cleanly release the held operation without trying
        // to clone+reinject. Cloning a possibly-stale NBL after the TCP
        // layer has given up is racy (caused IRQL_NOT_LESS_OR_EQUAL during
        // testing). The IOCTL path handles the live-connection case; the
        // timeout path is just a safety net to release the kernel hold.
        ScamAlertBlockAndRelease(node);
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
    LONGLONG nowIn100ns = now.QuadPart * KeQueryTimeIncrement();

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

        if ((nowIn100ns - node->PendedAtTicks.QuadPart) > ScamAlertPendingTimeoutTicks)
        {
            RemoveEntryList(&node->Link);

            SCAMALERT_TIMEOUT_BATCH* item = static_cast<SCAMALERT_TIMEOUT_BATCH*>(
                ExAllocatePool2(POOL_FLAG_NON_PAGED, sizeof(SCAMALERT_TIMEOUT_BATCH), SCAMALERT_POOL_TAG));
            if (item != nullptr)
            {
                item->Node = node;
                InsertTailList(batchHead, &item->Entry);
                any = TRUE;
            }
            else
            {
                // Out of memory while building the batch: fall back to a
                // blocking release inline (this still runs at DISPATCH but
                // FwpsCompleteOperation0 tolerates it).
                if (node->CompletionContext != nullptr)
                {
                    FwpsCompleteOperation0(node->CompletionContext, nullptr);
                    node->CompletionContext = nullptr;
                }
                ScamAlertReleasePendingNode(node);
            }
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

    g_PendingInitialized = FALSE;

    KeCancelTimer(&g_TimeoutTimer);
    KeFlushQueuedDpcs();

    if (g_TimeoutWorkItem != nullptr)
    {
        IoFreeWorkItem(g_TimeoutWorkItem);
        g_TimeoutWorkItem = nullptr;
    }

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

    // On destroy we fail BLOCK rather than fail-open. Reinjecting through a
    // partially torn-down WFP stack is too risky; the user explicitly
    // stopped the driver. Held connections die cleanly.
    while (!IsListEmpty(&drainList))
    {
        PLIST_ENTRY entry = RemoveHeadList(&drainList);
        SCAMALERT_PENDING_NODE* node = CONTAINING_RECORD(entry, SCAMALERT_PENDING_NODE, Link);
        ScamAlertBlockAndRelease(node);
    }
}

NTSTATUS ScamAlertAddPendingOp(_In_ SCAMALERT_PENDING_NODE* Node)
{
    if (Node == nullptr || Node->CompletionContext == nullptr || Node->NetBufferList == nullptr)
    {
        return STATUS_INVALID_PARAMETER;
    }

    LARGE_INTEGER ticks;
    KeQueryTickCount(&ticks);
    Node->PendedAtTicks.QuadPart = ticks.QuadPart * KeQueryTimeIncrement();

    KIRQL oldIrql;
    KeAcquireSpinLock(&g_PendingLock, &oldIrql);
    InsertTailList(&g_PendingList, &Node->Link);
    KeReleaseSpinLock(&g_PendingLock, oldIrql);

    return STATUS_SUCCESS;
}

// ALLOW path: clone the saved inbound NBL, hand the clone to
// FwpsCompleteOperation0 (so WFP knows to release the held operation as
// permitted), and FwpsInjectTransportReceiveAsync0 the clone back into the
// stack. Our classifyFn detects the self-injection via
// FwpsQueryPacketInjectionState0 and short-circuits to PERMIT.
//
// On any failure inside this helper, ownership of `node` and its
// CompletionContext stays with the caller (we do not consume it), so the
// caller can fall back to BLOCK. Successful inject transfers ownership of
// the node to the inject completion routine.
static NTSTATUS ScamAlertAllowReinjectInbound(_Inout_ SCAMALERT_PENDING_NODE* node)
{
    if (node == nullptr || node->NetBufferList == nullptr || node->CompletionContext == nullptr)
    {
        return STATUS_INVALID_PARAMETER;
    }

    HANDLE injectionHandle = ScamAlertGetInjectionHandle();
    if (injectionHandle == nullptr)
    {
        return STATUS_INVALID_DEVICE_STATE;
    }

    NTSTATUS status            = STATUS_SUCCESS;
    NET_BUFFER_LIST* clonedNbl = nullptr;
    BOOLEAN advanced           = FALSE;

    NET_BUFFER* netBuffer = NET_BUFFER_LIST_FIRST_NB(node->NetBufferList);

    // The TCP/IP stack may have already retreated the NBL by the transport
    // header size; mirror the inspect sample's logic and only retreat the
    // delta so we don't double-retreat into corruption territory.
    ULONG currentOffset = NET_BUFFER_DATA_OFFSET(netBuffer);
    UINT32 effectiveTransport = node->TransportHeaderSize;
    if (currentOffset != node->NblOffset)
    {
        effectiveTransport = 0;
    }

    NDIS_STATUS ndisStatus = NdisRetreatNetBufferDataStart(
        netBuffer,
        node->IpHeaderSize + effectiveTransport,
        0,
        nullptr);
    if (ndisStatus != NDIS_STATUS_SUCCESS)
    {
        status = STATUS_UNSUCCESSFUL;
        goto Exit;
    }
    advanced = TRUE;

    status = FwpsAllocateCloneNetBufferList0(
        node->NetBufferList, nullptr, nullptr, 0, &clonedNbl);

    // Always undo the retreat on the original, even on clone failure.
    NdisAdvanceNetBufferDataStart(
        netBuffer,
        node->IpHeaderSize + effectiveTransport,
        FALSE,
        nullptr);
    advanced = FALSE;

    if (!NT_SUCCESS(status) || clonedNbl == nullptr)
    {
        goto Exit;
    }

    // Hand the clone to FwpsCompleteOperation0 so WFP knows the held
    // operation is being released as permitted.
    FwpsCompleteOperation0(node->CompletionContext, clonedNbl);
    node->CompletionContext = nullptr;

    status = FwpsInjectTransportReceiveAsync0(
        injectionHandle,
        nullptr,
        nullptr,
        0,
        node->AddressFamily,
        node->CompartmentId,
        node->InterfaceIndex,
        node->SubInterfaceIndex,
        clonedNbl,
        ScamAlertInjectComplete,
        node);

    if (!NT_SUCCESS(status))
    {
        goto Exit;
    }

    // Both clone and node ownership transferred to the inject completion fn.
    clonedNbl = nullptr;
    ScamAlertBumpAllowInjected();
    return STATUS_SUCCESS;

Exit:
    if (advanced)
    {
        NdisAdvanceNetBufferDataStart(
            netBuffer,
            node->IpHeaderSize + effectiveTransport,
            FALSE,
            nullptr);
    }
    if (clonedNbl != nullptr)
    {
        FwpsFreeCloneNetBufferList0(clonedNbl, 0);
    }
    return status;
}

// BLOCK path: tell WFP "release the held operation, no clone" and drop our
// saved NBL reference. The original packet stays blocked from the
// classifyOut value set during the original classify hit.
static VOID ScamAlertBlockAndRelease(_In_ SCAMALERT_PENDING_NODE* node)
{
    if (node == nullptr) return;

    if (node->CompletionContext != nullptr)
    {
        FwpsCompleteOperation0(node->CompletionContext, nullptr);
        node->CompletionContext = nullptr;
    }
    ScamAlertBumpBlockReleased();
    ScamAlertReleasePendingNode(node);
}

NTSTATUS ScamAlertCompletePendingOp(
    _In_ const UINT8*               EventId,
    _In_ SCAMALERT_DRIVER_DECISION  Decision)
{
    if (EventId == nullptr) return STATUS_INVALID_PARAMETER;

    SCAMALERT_PENDING_NODE* node = nullptr;

    KIRQL oldIrql;
    KeAcquireSpinLock(&g_PendingLock, &oldIrql);

    PLIST_ENTRY entry = g_PendingList.Flink;
    while (entry != &g_PendingList)
    {
        SCAMALERT_PENDING_NODE* candidate = CONTAINING_RECORD(entry, SCAMALERT_PENDING_NODE, Link);
        if (RtlEqualMemory(candidate->EventId, EventId, 16))
        {
            RemoveEntryList(&candidate->Link);
            node = candidate;
            break;
        }
        entry = entry->Flink;
    }

    KeReleaseSpinLock(&g_PendingLock, oldIrql);

    if (node == nullptr)
    {
        // Already completed (timeout, duplicate IOCTL) - silently succeed.
        return STATUS_SUCCESS;
    }

    if (Decision == ScamAlertDecisionAllow)
    {
        NTSTATUS s = ScamAlertAllowReinjectInbound(node);
        if (!NT_SUCCESS(s))
        {
            // Couldn't reinject - the safe fallback is to block.
            ScamAlertBlockAndRelease(node);
        }
        return STATUS_SUCCESS;
    }

    ScamAlertBlockAndRelease(node);
    return STATUS_SUCCESS;
}
