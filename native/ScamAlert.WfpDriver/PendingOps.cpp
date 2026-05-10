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
static LONG           g_PendingCount         = 0;
static constexpr LONG ScamAlertMaxPendingOps = 256;

static KTIMER         g_TimeoutTimer;
static KDPC           g_TimeoutDpc;
static PIO_WORKITEM   g_TimeoutWorkItem      = nullptr;
static PDEVICE_OBJECT g_PendingDeviceObject  = nullptr;
static LIST_ENTRY     g_TimeoutList;
static KSPIN_LOCK     g_TimeoutLock;
static volatile LONG  g_TimeoutWorkQueued    = 0;
static KEVENT         g_TimeoutWorkDrained;

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

static VOID NTAPI ScamAlertTimeoutWorkRoutine(
    _In_ PDEVICE_OBJECT DeviceObject,
    _In_opt_ PVOID Context)
{
    UNREFERENCED_PARAMETER(DeviceObject);
    UNREFERENCED_PARAMETER(Context);

    for (;;)
    {
        LIST_ENTRY drainList;
        InitializeListHead(&drainList);

        KIRQL oldIrql;
        KeAcquireSpinLock(&g_TimeoutLock, &oldIrql);
        while (!IsListEmpty(&g_TimeoutList))
        {
            PLIST_ENTRY entry = RemoveHeadList(&g_TimeoutList);
            InsertTailList(&drainList, entry);
        }

        if (IsListEmpty(&drainList))
        {
            InterlockedExchange(&g_TimeoutWorkQueued, 0);
            KeSetEvent(&g_TimeoutWorkDrained, IO_NO_INCREMENT, FALSE);
            KeReleaseSpinLock(&g_TimeoutLock, oldIrql);
            return;
        }
        KeReleaseSpinLock(&g_TimeoutLock, oldIrql);

        while (!IsListEmpty(&drainList))
        {
            PLIST_ENTRY entry = RemoveHeadList(&drainList);
            SCAMALERT_PENDING_NODE* node = CONTAINING_RECORD(entry, SCAMALERT_PENDING_NODE, Link);

            if (node == nullptr) continue;

            ScamAlertBumpTimedOutFailBlock();

            // Fail-BLOCK: cleanly release the held operation without trying
            // to clone+reinject. Cloning a possibly-stale NBL after the TCP
            // layer has given up is racy (caused IRQL_NOT_LESS_OR_EQUAL during
            // testing). The IOCTL path handles the live-connection case; the
            // timeout path is just a safety net to release the kernel hold.
            ScamAlertBlockAndRelease(node);
        }
    }
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

    LIST_ENTRY expiredList;
    InitializeListHead(&expiredList);

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
            if (g_PendingCount > 0)
            {
                --g_PendingCount;
            }
            InsertTailList(&expiredList, &node->Link);
        }

        entry = next;
    }

    KeReleaseSpinLock(&g_PendingLock, oldIrql);

    if (IsListEmpty(&expiredList))
    {
        return;
    }

    KeAcquireSpinLock(&g_TimeoutLock, &oldIrql);
    while (!IsListEmpty(&expiredList))
    {
        PLIST_ENTRY expiredEntry = RemoveHeadList(&expiredList);
        InsertTailList(&g_TimeoutList, expiredEntry);
    }
    KeReleaseSpinLock(&g_TimeoutLock, oldIrql);

    if (InterlockedCompareExchange(&g_TimeoutWorkQueued, 1, 0) == 0)
    {
        KeClearEvent(&g_TimeoutWorkDrained);
        IoQueueWorkItem(g_TimeoutWorkItem, ScamAlertTimeoutWorkRoutine, DelayedWorkQueue, nullptr);
    }
}

NTSTATUS ScamAlertInitializePendingOps()
{
    if (g_PendingInitialized) return STATUS_SUCCESS;

    InitializeListHead(&g_PendingList);
    KeInitializeSpinLock(&g_PendingLock);
    g_PendingCount = 0;
    InitializeListHead(&g_TimeoutList);
    KeInitializeSpinLock(&g_TimeoutLock);
    InterlockedExchange(&g_TimeoutWorkQueued, 0);
    KeInitializeEvent(&g_TimeoutWorkDrained, NotificationEvent, TRUE);

    g_PendingDeviceObject = ScamAlertGetDeviceObject();
    if (g_PendingDeviceObject == nullptr)
    {
        return STATUS_INVALID_DEVICE_STATE;
    }

    g_TimeoutWorkItem = IoAllocateWorkItem(g_PendingDeviceObject);
    if (g_TimeoutWorkItem == nullptr)
    {
        return STATUS_INSUFFICIENT_RESOURCES;
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
        KeWaitForSingleObject(&g_TimeoutWorkDrained, Executive, KernelMode, FALSE, nullptr);
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
        if (g_PendingCount > 0)
        {
            --g_PendingCount;
        }
        InsertTailList(&drainList, entry);
    }
    KeReleaseSpinLock(&g_PendingLock, oldIrql);

    KeAcquireSpinLock(&g_TimeoutLock, &oldIrql);
    while (!IsListEmpty(&g_TimeoutList))
    {
        PLIST_ENTRY entry = RemoveHeadList(&g_TimeoutList);
        InsertTailList(&drainList, entry);
    }
    KeReleaseSpinLock(&g_TimeoutLock, oldIrql);
    g_PendingCount = 0;

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

BOOLEAN ScamAlertHasPendingCapacity()
{
    if (!g_PendingInitialized) return FALSE;

    BOOLEAN hasCapacity;
    KIRQL oldIrql;
    KeAcquireSpinLock(&g_PendingLock, &oldIrql);
    hasCapacity = g_PendingCount < ScamAlertMaxPendingOps;
    KeReleaseSpinLock(&g_PendingLock, oldIrql);
    return hasCapacity;
}

NTSTATUS ScamAlertAddPendingOp(_In_ SCAMALERT_PENDING_NODE* Node)
{
    if (Node == nullptr || Node->CompletionContext == nullptr || Node->NetBufferList == nullptr)
    {
        return STATUS_INVALID_PARAMETER;
    }

    if (!g_PendingInitialized)
    {
        return STATUS_DEVICE_NOT_READY;
    }

    LARGE_INTEGER ticks;
    KeQueryTickCount(&ticks);
    Node->PendedAtTicks.QuadPart = ticks.QuadPart * KeQueryTimeIncrement();

    KIRQL oldIrql;
    KeAcquireSpinLock(&g_PendingLock, &oldIrql);

    if (g_PendingCount >= ScamAlertMaxPendingOps)
    {
        KeReleaseSpinLock(&g_PendingLock, oldIrql);
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    InsertTailList(&g_PendingList, &Node->Link);
    ++g_PendingCount;
    KeReleaseSpinLock(&g_PendingLock, oldIrql);

    return STATUS_SUCCESS;
}

VOID ScamAlertCancelPendingOp(_In_ SCAMALERT_PENDING_NODE* Node)
{
    if (Node == nullptr) return;

    BOOLEAN removed = FALSE;
    KIRQL oldIrql;
    KeAcquireSpinLock(&g_PendingLock, &oldIrql);

    PLIST_ENTRY entry = g_PendingList.Flink;
    while (entry != &g_PendingList)
    {
        PLIST_ENTRY next = entry->Flink;
        SCAMALERT_PENDING_NODE* candidate = CONTAINING_RECORD(entry, SCAMALERT_PENDING_NODE, Link);
        if (candidate == Node)
        {
            RemoveEntryList(&candidate->Link);
            if (g_PendingCount > 0)
            {
                --g_PendingCount;
            }
            removed = TRUE;
            break;
        }
        entry = next;
    }

    KeReleaseSpinLock(&g_PendingLock, oldIrql);

    if (!removed)
    {
        return;
    }

    if (Node->CompletionContext != nullptr)
    {
        FwpsCompleteOperation0(Node->CompletionContext, nullptr);
        Node->CompletionContext = nullptr;
    }

    ScamAlertReleasePendingNode(Node);
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
            if (g_PendingCount > 0)
            {
                --g_PendingCount;
            }
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
