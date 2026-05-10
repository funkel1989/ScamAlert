#pragma once

#include <ntddk.h>
#include "..\ScamAlert.Driver.Shared\ScamAlertDriverIoctl.h"

// Pool tag used for every allocation owned by the queue. Visible from
// `!poolused` / pool tag triage as 'ScSa' (memory is little-endian, so
// 'aScS' as a four-character literal shows up reversed).
#define SCAMALERT_POOL_TAG 'aScS'

typedef struct SCAMALERT_EVENT_NODE
{
    LIST_ENTRY                  Link;
    SCAMALERT_CONNECTION_EVENT  Event;
} SCAMALERT_EVENT_NODE;

NTSTATUS ScamAlertInitializeEventQueue();
VOID     ScamAlertDestroyEventQueue();

NTSTATUS ScamAlertQueueConnectionEvent(_In_  const SCAMALERT_CONNECTION_EVENT* Event);
NTSTATUS ScamAlertPopConnectionEvent  (_Out_       SCAMALERT_CONNECTION_EVENT* Event);
NTSTATUS ScamAlertCompleteConnectionEvent(_In_ const SCAMALERT_CONNECTION_DECISION* Decision);
