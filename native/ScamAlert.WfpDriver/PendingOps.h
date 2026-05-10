#pragma once

#include <ntddk.h>
#include <fwpsk.h>      // ADDRESS_FAMILY, COMPARTMENT_ID, NET_BUFFER_LIST*, IF_INDEX
#include "..\ScamAlert.Driver.Shared\ScamAlertDriverIoctl.h"

// Per-pending-classify state for the FwpsPendOperation0 + clone-and-reinject
// flow at FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V{4,6}. There is NO reauth at this
// layer; verdict delivery works as follows:
//
//   * ALLOW: classify clones the inbound NBL and pends. When user mode says
//            allow, we FwpsAllocateCloneNetBufferList0 the saved NBL, hand
//            the clone to FwpsCompleteOperation0(ctx, clone), then reinject
//            it via FwpsInjectTransportReceiveAsync0. Our classifyFn re-runs
//            on the reinject and detects the self-injection via
//            FwpsQueryPacketInjectionState0, short-circuiting to PERMIT.
//
//   * BLOCK: FwpsCompleteOperation0(ctx, NULL); the original packet stays
//            blocked from the classifyOut set during the original classify.
//
// Everything required to reinject (NBL ref, family, compartment, interface,
// IP/transport header sizes, NBL offset) gets captured at classify time.
typedef struct SCAMALERT_PENDING_NODE
{
    LIST_ENTRY        Link;
    UINT8             EventId[16];

    HANDLE            CompletionContext;     // OUT of FwpsPendOperation0
    NET_BUFFER_LIST*  NetBufferList;         // referenced via FwpsReferenceNetBufferList0
    ADDRESS_FAMILY    AddressFamily;
    COMPARTMENT_ID    CompartmentId;
    IF_INDEX          InterfaceIndex;
    IF_INDEX          SubInterfaceIndex;
    UINT32            IpHeaderSize;
    UINT32            TransportHeaderSize;
    ULONG             NblOffset;             // NET_BUFFER_DATA_OFFSET captured at classify time

    UINT16            LayerId;
    LARGE_INTEGER     PendedAtTicks;
} SCAMALERT_PENDING_NODE;

NTSTATUS ScamAlertInitializePendingOps();
VOID     ScamAlertDestroyPendingOps();
BOOLEAN  ScamAlertHasPendingCapacity();

// Insert a fully-populated node into the pending table. The caller must have
// already called FwpsPendOperation0 and FwpsReferenceNetBufferList0; the
// table assumes ownership of both for the rest of the node's lifetime.
NTSTATUS ScamAlertAddPendingOp(_In_ SCAMALERT_PENDING_NODE* Node);

// Roll back a node that was inserted into the pending table but cannot be
// exposed to user mode. Removes the node, completes the held operation, and
// releases the saved NBL reference without counting it as an allow/block
// verdict.
VOID ScamAlertCancelPendingOp(_In_ SCAMALERT_PENDING_NODE* Node);

// User-mode IOCTL hand-off. Looks up the entry by EventId, removes it from
// the pending list, and dispatches to the ALLOW (clone+reinject) or BLOCK
// (FwpsCompleteOperation0 + drop) path. Always called at PASSIVE_LEVEL.
NTSTATUS ScamAlertCompletePendingOp(
    _In_ const UINT8*               EventId,
    _In_ SCAMALERT_DRIVER_DECISION  Decision);
