#pragma once

#include <ntddk.h>
#include "..\ScamAlert.Driver.Shared\ScamAlertDriverIoctl.h"

// Per-pending-classify state. The classify handle is what FwpsCompleteClassify0
// needs to deliver the deferred verdict, so we key the table by EventId
// (the same UUID that's queued to user mode and round-tripped through the
// broker).
typedef struct SCAMALERT_PENDING_NODE
{
    LIST_ENTRY    Link;
    UINT8         EventId[16];
    UINT64        ClassifyHandle;   // FwpsAcquireClassifyHandle0 returns UINT64 in WDK 26100+.
    UINT16        LayerId;
    LARGE_INTEGER PendedAtTicks;
} SCAMALERT_PENDING_NODE;

NTSTATUS ScamAlertInitializePendingOps();
VOID     ScamAlertDestroyPendingOps();

NTSTATUS ScamAlertAddPendingOp(
    _In_ const UINT8* EventId,
    _In_ UINT64       ClassifyHandle,
    _In_ UINT16       LayerId);

NTSTATUS ScamAlertCompletePendingOp(
    _In_ const UINT8*               EventId,
    _In_ SCAMALERT_DRIVER_DECISION  Decision);
