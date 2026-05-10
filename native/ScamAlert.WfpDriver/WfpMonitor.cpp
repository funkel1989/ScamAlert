#include "WfpMonitor.h"

// initguid.h must come before any header containing DEFINE_GUID so this
// translation unit allocates storage for the GUID symbols (both ours
// and the FWPM_* layer/condition GUIDs that fwpmk.h declares). Only one
// compilation unit in the driver should include initguid.h.
#include <initguid.h>
#include <fwpsk.h>
#include <fwpmk.h>

#include "EventQueue.h"
#include "PendingOps.h"
#include "..\ScamAlert.Driver.Shared\ScamAlertDriverIoctl.h"

// Stable GUIDs published in the WFP integration contract / plan.

// {585493A7-CF45-4551-ABCF-111BA6007130} - IPv4 callout key.
DEFINE_GUID(SCAMALERT_CALLOUT_RECV_ACCEPT_V4,
    0x585493a7, 0xcf45, 0x4551, 0xab, 0xcf, 0x11, 0x1b, 0xa6, 0x00, 0x71, 0x30);

// {BA321121-D6AF-4AA9-907C-F365D7C2684A} - IPv6 callout key.
DEFINE_GUID(SCAMALERT_CALLOUT_RECV_ACCEPT_V6,
    0xba321121, 0xd6af, 0x4aa9, 0x90, 0x7c, 0xf3, 0x65, 0xd7, 0xc2, 0x68, 0x4a);

// {653537B3-4364-4E17-A99C-45F31AF2B9ED} - sublayer key.
DEFINE_GUID(SCAMALERT_SUBLAYER,
    0x653537b3, 0x4364, 0x4e17, 0xa9, 0x9c, 0x45, 0xf3, 0x1a, 0xf2, 0xb9, 0xed);

// {B4EDE861-10F7-4103-8951-94D253F7AE67} - provider key.
DEFINE_GUID(SCAMALERT_PROVIDER,
    0xb4ede861, 0x10f7, 0x4103, 0x89, 0x51, 0x94, 0xd2, 0x53, 0xf7, 0xae, 0x67);

static HANDLE  g_EngineHandle        = nullptr;
static HANDLE  g_InjectionHandle     = nullptr;
static BOOLEAN g_CalloutV4Registered = FALSE;
static BOOLEAN g_CalloutV6Registered = FALSE;
static UINT32  g_CalloutV4Id         = 0;
static UINT32  g_CalloutV6Id         = 0;
static UINT64  g_FilterV4Id          = 0;
static UINT64  g_FilterV6Id          = 0;

static volatile LONG64 g_NextEventId = 0;

HANDLE ScamAlertGetInjectionHandle()
{
    return g_InjectionHandle;
}

// ---------- helpers ----------

static ULONGLONG ScamAlertUnixTimeMilliseconds()
{
    LARGE_INTEGER systemTime;
    KeQuerySystemTimePrecise(&systemTime);
    constexpr LONGLONG UnixEpochOffsetTicks = 116444736000000000LL;
    return static_cast<ULONGLONG>((systemTime.QuadPart - UnixEpochOffsetTicks) / 10000);
}

static VOID ScamAlertWriteEventId(_Out_writes_bytes_(16) UINT8* eventId)
{
    const ULONGLONG sequence = static_cast<ULONGLONG>(InterlockedIncrement64(&g_NextEventId));

    LARGE_INTEGER systemTime;
    KeQuerySystemTimePrecise(&systemTime);

    RtlZeroMemory(eventId, 16);
    RtlCopyMemory(eventId, &sequence, sizeof(sequence));
    RtlCopyMemory(eventId + sizeof(sequence), &systemTime.QuadPart, sizeof(systemTime.QuadPart));
}

static BOOLEAN ScamAlertTryGetService(UINT16 localPort, _Out_ SCAMALERT_PROTECTED_SERVICE* service)
{
    switch (localPort)
    {
    case 3389: *service = ScamAlertServiceRdp;    return TRUE;
    case 22:   *service = ScamAlertServiceSsh;    return TRUE;
    case 23:   *service = ScamAlertServiceTelnet; return TRUE;
    default:                                       return FALSE;
    }
}

// classifyFn fires from DPC context (network indication), so EVERYTHING it
// touches must be non-paged. The ntstrsafe.h printf family lives in paged
// code (Rtl* -> woutput_l -> RtlpIsUtf8Process), so calling it from
// classifyFn page-faults at IRQL=2 and bugchecks. We hand-roll the two
// formatters we need below; they're tiny and IRQL-agnostic.

// Writes an unsigned decimal value, returns characters written (excluding
// the trailing NUL). Returns 0 if the destination can't hold value+NUL.
static SIZE_T ScamAlertWriteUInt32Decimal(
    _Out_writes_(maxChars) wchar_t* dest,
    UINT32                          value,
    SIZE_T                          maxChars)
{
    wchar_t scratch[11];               // max u32 is "4294967295" -> 10 digits
    SIZE_T  digits = 0;
    do
    {
        scratch[digits++] = static_cast<wchar_t>(L'0' + (value % 10));
        value /= 10;
    } while (value != 0 && digits < ARRAYSIZE(scratch));

    if (digits + 1 > maxChars) return 0;

    SIZE_T offset = 0;
    for (SIZE_T i = digits; i > 0; --i)
    {
        dest[offset++] = scratch[i - 1];
    }
    return offset;
}

// Writes a single byte as two lowercase hex digits.
static SIZE_T ScamAlertWriteByteHex(
    _Out_writes_(maxChars) wchar_t* dest,
    UINT8                           value,
    SIZE_T                          maxChars)
{
    if (maxChars < 2) return 0;
    static const wchar_t kHex[] = L"0123456789abcdef";
    dest[0] = kHex[(value >> 4) & 0xf];
    dest[1] = kHex[ value       & 0xf];
    return 2;
}

static NTSTATUS ScamAlertWriteIpv4Source(UINT32 sourceAddressHostOrder, _Out_writes_(SCAMALERT_MAX_IP_CHARS) wchar_t* sourceIp)
{
    // WFP delivers ALE_AUTH_RECV_ACCEPT_V4 IP_REMOTE_ADDRESS in host byte order.
    const UINT32 octet[4] = {
        (sourceAddressHostOrder >> 24) & 0xff,
        (sourceAddressHostOrder >> 16) & 0xff,
        (sourceAddressHostOrder >>  8) & 0xff,
         sourceAddressHostOrder        & 0xff
    };

    wchar_t* p         = sourceIp;
    SIZE_T   remaining = SCAMALERT_MAX_IP_CHARS;

    for (int i = 0; i < 4; ++i)
    {
        SIZE_T written = ScamAlertWriteUInt32Decimal(p, octet[i], remaining);
        if (written == 0) return STATUS_BUFFER_TOO_SMALL;
        p         += written;
        remaining -= written;

        if (i < 3)
        {
            if (remaining < 2) return STATUS_BUFFER_TOO_SMALL;
            *p++ = L'.';
            --remaining;
        }
    }

    if (remaining < 1) return STATUS_BUFFER_TOO_SMALL;
    *p = L'\0';
    return STATUS_SUCCESS;
}

static NTSTATUS ScamAlertWriteIpv6Source(const FWP_BYTE_ARRAY16* sourceAddress, _Out_writes_(SCAMALERT_MAX_IP_CHARS) wchar_t* sourceIp)
{
    if (sourceAddress == nullptr)
    {
        return STATUS_INVALID_PARAMETER;
    }

    const UINT8* b = sourceAddress->byteArray16;

    wchar_t* p         = sourceIp;
    SIZE_T   remaining = SCAMALERT_MAX_IP_CHARS;

    // 8 colon-separated 16-bit groups, each rendered as 4 lowercase hex digits.
    for (int group = 0; group < 8; ++group)
    {
        SIZE_T w1 = ScamAlertWriteByteHex(p, b[group * 2],     remaining);
        if (w1 == 0) return STATUS_BUFFER_TOO_SMALL;
        p += w1; remaining -= w1;

        SIZE_T w2 = ScamAlertWriteByteHex(p, b[group * 2 + 1], remaining);
        if (w2 == 0) return STATUS_BUFFER_TOO_SMALL;
        p += w2; remaining -= w2;

        if (group < 7)
        {
            if (remaining < 2) return STATUS_BUFFER_TOO_SMALL;
            *p++ = L':';
            --remaining;
        }
    }

    if (remaining < 1) return STATUS_BUFFER_TOO_SMALL;
    *p = L'\0';
    return STATUS_SUCCESS;
}

static BOOLEAN ScamAlertIsReauthorize(const FWPS_INCOMING_METADATA_VALUES0* inMetaValues)
{
    // FWPS_METADATA_FIELD_SYSTEM_FLAGS gates the `flags` member of
    // FWPS_INCOMING_METADATA_VALUES0. The WDK 10.0.28000+ headers
    // dropped the older FWPS_METADATA_FIELD_FLAGS spelling.
    return (inMetaValues->currentMetadataValues & FWPS_METADATA_FIELD_SYSTEM_FLAGS) != 0 &&
           (inMetaValues->flags & FWP_CONDITION_FLAG_IS_REAUTHORIZE) != 0;
}

// ---------- classify functions ----------

// Diagnostic counters for the pend-and-reinject path. ScamAlertGetMonitorCounters
// exposes them via the IOCTL_SCAMALERT_GET_STATS diagnostic call.
static volatile LONG64 g_StatsClassifyEntered     = 0;
static volatile LONG64 g_StatsSelfInjectedSkipped = 0;
static volatile LONG64 g_StatsEventsQueued        = 0;
static volatile LONG64 g_StatsPendOk              = 0;
static volatile LONG64 g_StatsAllowInjected       = 0;
static volatile LONG64 g_StatsBlockReleased       = 0;
static volatile LONG64 g_StatsTimedOutFailBlock   = 0;
static volatile LONG64 g_StatsEventsDropped       = 0;
static volatile LONG64 g_StatsPendingRejected     = 0;

VOID ScamAlertGetMonitorCounters(_Out_ LONG64* out)
{
    out[0] = g_StatsClassifyEntered;
    out[1] = g_StatsSelfInjectedSkipped;
    out[2] = g_StatsEventsQueued;
    out[3] = g_StatsPendOk;
    out[4] = g_StatsAllowInjected;
    out[5] = g_StatsBlockReleased;
    out[6] = g_StatsTimedOutFailBlock;
    out[7] = g_StatsEventsDropped;
    out[8] = g_StatsPendingRejected;
}

VOID ScamAlertBumpAllowInjected()    { InterlockedIncrement64(&g_StatsAllowInjected); }
VOID ScamAlertBumpBlockReleased()    { InterlockedIncrement64(&g_StatsBlockReleased); }
VOID ScamAlertBumpTimedOutFailBlock(){ InterlockedIncrement64(&g_StatsTimedOutFailBlock); }
VOID ScamAlertBumpEventsDropped()    { InterlockedIncrement64(&g_StatsEventsDropped); }
VOID ScamAlertBumpPendingRejected()  { InterlockedIncrement64(&g_StatsPendingRejected); }

// Returns TRUE if WFP gave us a packet that was injected by our own driver.
// At ALE_AUTH_RECV_ACCEPT this is the second classify hit on a permitted
// connection (the clone we reinjected), and we short-circuit to PERMIT
// rather than queueing the user another decision prompt.
static BOOLEAN ScamAlertIsSelfInjected(_In_opt_ void* layerData)
{
    if (g_InjectionHandle == nullptr || layerData == nullptr) return FALSE;

    FWPS_PACKET_INJECTION_STATE state = FwpsQueryPacketInjectionState0(
        g_InjectionHandle, static_cast<NET_BUFFER_LIST*>(layerData), nullptr);

    return state == FWPS_PACKET_INJECTED_BY_SELF ||
           state == FWPS_PACKET_PREVIOUSLY_INJECTED_BY_SELF;
}

// Reads the per-layer interface index fields (different fixed-value indices
// for V4 vs V6). Caller passes the layer-specific FWPS_FIELD_* enum values.
static VOID ScamAlertReadInterfaceIndices(
    _In_ const FWPS_INCOMING_VALUES0* inFixedValues,
    _In_ UINT32                       interfaceFieldIndex,
    _In_ UINT32                       subInterfaceFieldIndex,
    _Out_ IF_INDEX*                   interfaceIndex,
    _Out_ IF_INDEX*                   subInterfaceIndex)
{
    *interfaceIndex    = inFixedValues->incomingValue[interfaceFieldIndex].value.uint32;
    *subInterfaceIndex = inFixedValues->incomingValue[subInterfaceFieldIndex].value.uint32;
}

// Pends the operation so user mode can decide. Captures every NBL/offload
// field we'll need to clone+reinject the inbound packet later. For enforceable
// events, the pending node is inserted before the event is exposed to user mode
// so a fast bridge cannot complete an event before the kernel can find it.
// If any step fails we leave classifyOut on PERMIT (worst-case behavior is
// identical to Milestone A observe-only).
static VOID ScamAlertPendForBrokerDecision(
    _In_ const SCAMALERT_CONNECTION_EVENT*       event,
    _In_ const FWPS_INCOMING_VALUES0*            inFixedValues,
    _In_ const FWPS_INCOMING_METADATA_VALUES0*   inMetaValues,
    _Inout_opt_ void*                            layerData,
    _In_ ADDRESS_FAMILY                          addressFamily,
    _In_ UINT32                                  interfaceFieldIndex,
    _In_ UINT32                                  subInterfaceFieldIndex,
    _In_ UINT16                                  layerId,
    _Inout_ FWPS_CLASSIFY_OUT0*                  classifyOut)
{
    if (layerData == nullptr ||
        (inMetaValues->currentMetadataValues & FWPS_METADATA_FIELD_COMPLETION_HANDLE) == 0 ||
        (inMetaValues->currentMetadataValues & FWPS_METADATA_FIELD_IP_HEADER_SIZE) == 0 ||
        (inMetaValues->currentMetadataValues & FWPS_METADATA_FIELD_TRANSPORT_HEADER_SIZE) == 0 ||
        (inMetaValues->currentMetadataValues & FWPS_METADATA_FIELD_COMPARTMENT_ID) == 0)
    {
        if (NT_SUCCESS(ScamAlertQueueConnectionEvent(event)))
        {
            InterlockedIncrement64(&g_StatsEventsQueued);
        }
        else
        {
            ScamAlertBumpEventsDropped();
        }
        return;
    }

    if (!ScamAlertHasPendingCapacity())
    {
        ScamAlertBumpPendingRejected();
        return;
    }

    SCAMALERT_PENDING_NODE* node = static_cast<SCAMALERT_PENDING_NODE*>(
        ExAllocatePool2(POOL_FLAG_NON_PAGED, sizeof(SCAMALERT_PENDING_NODE), SCAMALERT_POOL_TAG));
    if (node == nullptr)
    {
        ScamAlertBumpPendingRejected();
        return;
    }

    RtlCopyMemory(node->EventId, event->EventId, 16);
    node->NetBufferList       = static_cast<NET_BUFFER_LIST*>(layerData);
    node->AddressFamily       = addressFamily;
    node->CompartmentId       = static_cast<COMPARTMENT_ID>(inMetaValues->compartmentId);
    node->IpHeaderSize        = inMetaValues->ipHeaderSize;
    node->TransportHeaderSize = inMetaValues->transportHeaderSize;
    node->LayerId             = layerId;

    ScamAlertReadInterfaceIndices(
        inFixedValues, interfaceFieldIndex, subInterfaceFieldIndex,
        &node->InterfaceIndex, &node->SubInterfaceIndex);

    NET_BUFFER* nb = NET_BUFFER_LIST_FIRST_NB(node->NetBufferList);
    node->NblOffset = (nb != nullptr) ? NET_BUFFER_DATA_OFFSET(nb) : 0;

    // Reference the NBL so it survives past the classify call. The pending
    // table releases the reference when the node is finally disposed.
    FwpsReferenceNetBufferList0(node->NetBufferList, TRUE);

    NTSTATUS status = FwpsPendOperation0(
        inMetaValues->completionHandle, &node->CompletionContext);
    if (!NT_SUCCESS(status) || node->CompletionContext == nullptr)
    {
        FwpsDereferenceNetBufferList0(node->NetBufferList, FALSE);
        ExFreePoolWithTag(node, SCAMALERT_POOL_TAG);
        ScamAlertBumpPendingRejected();
        return;
    }

    status = ScamAlertAddPendingOp(node);
    if (!NT_SUCCESS(status))
    {
        // Insertion failed: roll back the pend and drop the NBL ref.
        FwpsCompleteOperation0(node->CompletionContext, nullptr);
        FwpsDereferenceNetBufferList0(node->NetBufferList, FALSE);
        ExFreePoolWithTag(node, SCAMALERT_POOL_TAG);
        ScamAlertBumpPendingRejected();
        return;
    }

    if (!NT_SUCCESS(ScamAlertQueueConnectionEvent(event)))
    {
        ScamAlertCancelPendingOp(node);
        ScamAlertBumpEventsDropped();
        return;
    }

    InterlockedIncrement64(&g_StatsEventsQueued);
    InterlockedIncrement64(&g_StatsPendOk);

    // Tell WFP to hold this connection while we await the broker's verdict.
    // The verdict is delivered out of band via FwpsCompleteOperation0 +
    // FwpsInjectTransportReceiveAsync0 in ScamAlertCompletePendingOp.
    classifyOut->actionType = FWP_ACTION_BLOCK;
    classifyOut->rights    &= ~FWPS_RIGHT_ACTION_WRITE;
    classifyOut->flags     |= FWPS_CLASSIFY_OUT_FLAG_ABSORB;
}

static VOID NTAPI ScamAlertClassifyRecvAcceptV4(
    _In_ const FWPS_INCOMING_VALUES0*           inFixedValues,
    _In_ const FWPS_INCOMING_METADATA_VALUES0*  inMetaValues,
    _Inout_opt_ void*                            layerData,
    _In_opt_ const void*                         classifyContext,
    _In_ const FWPS_FILTER1*                     filter,
    _In_ UINT64                                  flowContext,
    _Inout_ FWPS_CLASSIFY_OUT0*                  classifyOut)
{
    UNREFERENCED_PARAMETER(layerData);
    UNREFERENCED_PARAMETER(flowContext);
    UNREFERENCED_PARAMETER(classifyContext);
    UNREFERENCED_PARAMETER(filter);

    InterlockedIncrement64(&g_StatsClassifyEntered);

    if ((classifyOut->rights & FWPS_RIGHT_ACTION_WRITE) == 0)
    {
        return;
    }

    classifyOut->actionType = FWP_ACTION_PERMIT;

    // The reinjected clone of an allowed connection re-enters classify;
    // recognize it and short-circuit to PERMIT without prompting again.
    if (ScamAlertIsSelfInjected(layerData))
    {
        InterlockedIncrement64(&g_StatsSelfInjectedSkipped);
        if (filter->flags & FWPS_FILTER_FLAG_CLEAR_ACTION_RIGHT)
        {
            classifyOut->rights &= ~FWPS_RIGHT_ACTION_WRITE;
        }
        return;
    }

    // ALE_AUTH_RECV_ACCEPT does not deliver verdicts via reauth; only the
    // first hit is relevant. Reauth (which fires on policy changes) is
    // simply permitted - we don't try to second-guess existing flows.
    if (ScamAlertIsReauthorize(inMetaValues))
    {
        return;
    }

    const UINT16 localPort = inFixedValues
        ->incomingValue[FWPS_FIELD_ALE_AUTH_RECV_ACCEPT_V4_IP_LOCAL_PORT]
        .value.uint16;

    SCAMALERT_PROTECTED_SERVICE service;
    if (!ScamAlertTryGetService(localPort, &service))
    {
        return;
    }

    SCAMALERT_CONNECTION_EVENT event = {};
    ScamAlertWriteEventId(event.EventId);
    event.OccurredAtUnixTimeMilliseconds = ScamAlertUnixTimeMilliseconds();
    event.DestinationPort  = localPort;
    event.ProtectedService = service;

    const UINT32 sourceAddress = inFixedValues
        ->incomingValue[FWPS_FIELD_ALE_AUTH_RECV_ACCEPT_V4_IP_REMOTE_ADDRESS]
        .value.uint32;

    if (!NT_SUCCESS(ScamAlertWriteIpv4Source(sourceAddress, reinterpret_cast<wchar_t*>(event.SourceIp))))
    {
        return;
    }

    ScamAlertPendForBrokerDecision(
        &event,
        inFixedValues,
        inMetaValues,
        layerData,
        AF_INET,
        FWPS_FIELD_ALE_AUTH_RECV_ACCEPT_V4_INTERFACE_INDEX,
        FWPS_FIELD_ALE_AUTH_RECV_ACCEPT_V4_SUB_INTERFACE_INDEX,
        FWPS_LAYER_ALE_AUTH_RECV_ACCEPT_V4,
        classifyOut);
}

static VOID NTAPI ScamAlertClassifyRecvAcceptV6(
    _In_ const FWPS_INCOMING_VALUES0*           inFixedValues,
    _In_ const FWPS_INCOMING_METADATA_VALUES0*  inMetaValues,
    _Inout_opt_ void*                            layerData,
    _In_opt_ const void*                         classifyContext,
    _In_ const FWPS_FILTER1*                     filter,
    _In_ UINT64                                  flowContext,
    _Inout_ FWPS_CLASSIFY_OUT0*                  classifyOut)
{
    UNREFERENCED_PARAMETER(layerData);
    UNREFERENCED_PARAMETER(flowContext);
    UNREFERENCED_PARAMETER(classifyContext);
    UNREFERENCED_PARAMETER(filter);

    InterlockedIncrement64(&g_StatsClassifyEntered);

    if ((classifyOut->rights & FWPS_RIGHT_ACTION_WRITE) == 0)
    {
        return;
    }

    classifyOut->actionType = FWP_ACTION_PERMIT;

    if (ScamAlertIsSelfInjected(layerData))
    {
        InterlockedIncrement64(&g_StatsSelfInjectedSkipped);
        if (filter->flags & FWPS_FILTER_FLAG_CLEAR_ACTION_RIGHT)
        {
            classifyOut->rights &= ~FWPS_RIGHT_ACTION_WRITE;
        }
        return;
    }

    if (ScamAlertIsReauthorize(inMetaValues))
    {
        return;
    }

    const UINT16 localPort = inFixedValues
        ->incomingValue[FWPS_FIELD_ALE_AUTH_RECV_ACCEPT_V6_IP_LOCAL_PORT]
        .value.uint16;

    SCAMALERT_PROTECTED_SERVICE service;
    if (!ScamAlertTryGetService(localPort, &service))
    {
        return;
    }

    SCAMALERT_CONNECTION_EVENT event = {};
    ScamAlertWriteEventId(event.EventId);
    event.OccurredAtUnixTimeMilliseconds = ScamAlertUnixTimeMilliseconds();
    event.DestinationPort  = localPort;
    event.ProtectedService = service;

    FWP_BYTE_ARRAY16 sourceAddress;
    RtlCopyMemory(
        sourceAddress.byteArray16,
        inFixedValues->incomingValue[FWPS_FIELD_ALE_AUTH_RECV_ACCEPT_V6_IP_REMOTE_ADDRESS].value.byteArray16->byteArray16,
        sizeof(sourceAddress.byteArray16));

    if (!NT_SUCCESS(ScamAlertWriteIpv6Source(&sourceAddress, reinterpret_cast<wchar_t*>(event.SourceIp))))
    {
        return;
    }

    ScamAlertPendForBrokerDecision(
        &event,
        inFixedValues,
        inMetaValues,
        layerData,
        AF_INET6,
        FWPS_FIELD_ALE_AUTH_RECV_ACCEPT_V6_INTERFACE_INDEX,
        FWPS_FIELD_ALE_AUTH_RECV_ACCEPT_V6_SUB_INTERFACE_INDEX,
        FWPS_LAYER_ALE_AUTH_RECV_ACCEPT_V6,
        classifyOut);
}

static NTSTATUS NTAPI ScamAlertNotify(
    _In_ FWPS_CALLOUT_NOTIFY_TYPE notifyType,
    _In_ const GUID*               filterKey,
    _Inout_ FWPS_FILTER1*           filter)
{
    UNREFERENCED_PARAMETER(notifyType);
    UNREFERENCED_PARAMETER(filterKey);
    UNREFERENCED_PARAMETER(filter);
    return STATUS_SUCCESS;
}

// ---------- WFP setup / teardown ----------

static NTSTATUS ScamAlertRegisterCallouts(_In_ PDEVICE_OBJECT DeviceObject)
{
    FWPS_CALLOUT1 calloutV4 = {};
    calloutV4.calloutKey      = SCAMALERT_CALLOUT_RECV_ACCEPT_V4;
    calloutV4.classifyFn      = ScamAlertClassifyRecvAcceptV4;
    calloutV4.notifyFn        = ScamAlertNotify;
    calloutV4.flowDeleteFn    = nullptr;
    calloutV4.flags           = 0;

    NTSTATUS status = FwpsCalloutRegister1(DeviceObject, &calloutV4, &g_CalloutV4Id);
    if (!NT_SUCCESS(status))
    {
        return status;
    }
    g_CalloutV4Registered = TRUE;

    FWPS_CALLOUT1 calloutV6 = {};
    calloutV6.calloutKey      = SCAMALERT_CALLOUT_RECV_ACCEPT_V6;
    calloutV6.classifyFn      = ScamAlertClassifyRecvAcceptV6;
    calloutV6.notifyFn        = ScamAlertNotify;
    calloutV6.flowDeleteFn    = nullptr;
    calloutV6.flags           = 0;

    status = FwpsCalloutRegister1(DeviceObject, &calloutV6, &g_CalloutV6Id);
    if (!NT_SUCCESS(status))
    {
        return status;
    }
    g_CalloutV6Registered = TRUE;

    return STATUS_SUCCESS;
}

static NTSTATUS ScamAlertAddProvider()
{
    FWPM_PROVIDER0 provider = {};
    provider.providerKey      = SCAMALERT_PROVIDER;
    provider.displayData.name = const_cast<wchar_t*>(L"ScamAlert WFP Monitor");
    provider.displayData.description = const_cast<wchar_t*>(L"Detects inbound RDP, SSH, Telnet attempts.");
    provider.flags = 0;

    return FwpmProviderAdd0(g_EngineHandle, &provider, nullptr);
}

static NTSTATUS ScamAlertAddSublayer()
{
    FWPM_SUBLAYER0 sublayer = {};
    sublayer.subLayerKey  = SCAMALERT_SUBLAYER;
    sublayer.displayData.name = const_cast<wchar_t*>(L"ScamAlert WFP Sublayer");
    sublayer.providerKey  = const_cast<GUID*>(&SCAMALERT_PROVIDER);
    sublayer.weight       = 0xFFFF;

    return FwpmSubLayerAdd0(g_EngineHandle, &sublayer, nullptr);
}

static NTSTATUS ScamAlertAddCalloutEntry(const GUID& calloutKey, const GUID& applicableLayer)
{
    FWPM_CALLOUT0 callout = {};
    callout.calloutKey       = calloutKey;
    callout.displayData.name = const_cast<wchar_t*>(L"ScamAlert recv-accept callout");
    callout.providerKey      = const_cast<GUID*>(&SCAMALERT_PROVIDER);
    callout.applicableLayer  = applicableLayer;

    return FwpmCalloutAdd0(g_EngineHandle, &callout, nullptr, nullptr);
}

static NTSTATUS ScamAlertAddFilter(
    const GUID& layerKey,
    const GUID& calloutKey,
    UINT16      conditionFieldId,
    UINT64*     filterIdOut)
{
    FWPM_FILTER_CONDITION0 portConditions[3] = {};
    portConditions[0].fieldKey = (layerKey == FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V4)
        ? FWPM_CONDITION_IP_LOCAL_PORT : FWPM_CONDITION_IP_LOCAL_PORT;
    portConditions[0].matchType = FWP_MATCH_EQUAL;
    portConditions[0].conditionValue.type   = FWP_UINT16;
    portConditions[0].conditionValue.uint16 = 3389;

    portConditions[1].fieldKey  = portConditions[0].fieldKey;
    portConditions[1].matchType = FWP_MATCH_EQUAL;
    portConditions[1].conditionValue.type   = FWP_UINT16;
    portConditions[1].conditionValue.uint16 = 22;

    portConditions[2].fieldKey  = portConditions[0].fieldKey;
    portConditions[2].matchType = FWP_MATCH_EQUAL;
    portConditions[2].conditionValue.type   = FWP_UINT16;
    portConditions[2].conditionValue.uint16 = 23;

    UNREFERENCED_PARAMETER(conditionFieldId);

    FWPM_FILTER0 filter = {};
    filter.layerKey               = layerKey;
    filter.subLayerKey            = SCAMALERT_SUBLAYER;
    filter.providerKey            = const_cast<GUID*>(&SCAMALERT_PROVIDER);
    filter.displayData.name       = const_cast<wchar_t*>(L"ScamAlert recv-accept filter");
    filter.action.type            = FWP_ACTION_CALLOUT_TERMINATING;
    filter.action.calloutKey      = calloutKey;
    filter.weight.type            = FWP_EMPTY;
    filter.numFilterConditions    = ARRAYSIZE(portConditions);
    filter.filterCondition        = portConditions;

    // WFP filter conditions are AND-combined; for OR semantics across
    // ports we install three single-port filters instead.
    NTSTATUS status = STATUS_SUCCESS;
    for (UINT32 i = 0; i < ARRAYSIZE(portConditions); i++)
    {
        FWPM_FILTER0 oneFilter = filter;
        oneFilter.numFilterConditions = 1;
        oneFilter.filterCondition     = &portConditions[i];

        UINT64 thisId = 0;
        NTSTATUS s = FwpmFilterAdd0(g_EngineHandle, &oneFilter, nullptr, &thisId);
        if (!NT_SUCCESS(s))
        {
            status = s;
        }
        else if (i == 0 && filterIdOut != nullptr)
        {
            *filterIdOut = thisId;
        }
    }
    return status;
}

NTSTATUS ScamAlertStartWfpMonitor(_In_ PDEVICE_OBJECT DeviceObject)
{
    // Injection handle must exist before classifyFn fires, since classify
    // queries it to recognize self-injected packets. AF_UNSPEC covers both
    // V4 and V6 reinjections.
    NTSTATUS status = FwpsInjectionHandleCreate0(
        AF_UNSPEC, FWPS_INJECTION_TYPE_TRANSPORT, &g_InjectionHandle);
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    status = ScamAlertRegisterCallouts(DeviceObject);
    if (!NT_SUCCESS(status))
    {
        ScamAlertStopWfpMonitor();
        return status;
    }

    FWPM_SESSION0 session = {};
    session.flags = FWPM_SESSION_FLAG_DYNAMIC;

    status = FwpmEngineOpen0(nullptr, RPC_C_AUTHN_WINNT, nullptr, &session, &g_EngineHandle);
    if (!NT_SUCCESS(status))
    {
        ScamAlertStopWfpMonitor();
        return status;
    }

    status = FwpmTransactionBegin0(g_EngineHandle, 0);
    if (!NT_SUCCESS(status))
    {
        ScamAlertStopWfpMonitor();
        return status;
    }

    status = ScamAlertAddProvider();
    if (!NT_SUCCESS(status)) goto rollback;

    status = ScamAlertAddSublayer();
    if (!NT_SUCCESS(status)) goto rollback;

    status = ScamAlertAddCalloutEntry(SCAMALERT_CALLOUT_RECV_ACCEPT_V4, FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V4);
    if (!NT_SUCCESS(status)) goto rollback;

    status = ScamAlertAddCalloutEntry(SCAMALERT_CALLOUT_RECV_ACCEPT_V6, FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V6);
    if (!NT_SUCCESS(status)) goto rollback;

    status = ScamAlertAddFilter(
        FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V4,
        SCAMALERT_CALLOUT_RECV_ACCEPT_V4,
        0,
        &g_FilterV4Id);
    if (!NT_SUCCESS(status)) goto rollback;

    status = ScamAlertAddFilter(
        FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V6,
        SCAMALERT_CALLOUT_RECV_ACCEPT_V6,
        0,
        &g_FilterV6Id);
    if (!NT_SUCCESS(status)) goto rollback;

    status = FwpmTransactionCommit0(g_EngineHandle);
    if (!NT_SUCCESS(status)) goto rollback;

    return STATUS_SUCCESS;

rollback:
    FwpmTransactionAbort0(g_EngineHandle);
    ScamAlertStopWfpMonitor();
    return status;
}

VOID ScamAlertStopWfpMonitor()
{
    if (g_EngineHandle != nullptr)
    {
        FwpmEngineClose0(g_EngineHandle);
        g_EngineHandle = nullptr;
    }

    if (g_CalloutV4Registered)
    {
        FwpsCalloutUnregisterByKey0(&SCAMALERT_CALLOUT_RECV_ACCEPT_V4);
        g_CalloutV4Registered = FALSE;
    }

    if (g_CalloutV6Registered)
    {
        FwpsCalloutUnregisterByKey0(&SCAMALERT_CALLOUT_RECV_ACCEPT_V6);
        g_CalloutV6Registered = FALSE;
    }

    if (g_InjectionHandle != nullptr)
    {
        FwpsInjectionHandleDestroy0(g_InjectionHandle);
        g_InjectionHandle = nullptr;
    }
}
