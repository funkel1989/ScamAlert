#include "WfpMonitor.h"

// initguid.h must come before any header containing DEFINE_GUID so this
// translation unit allocates storage for the GUID symbols (both ours
// and the FWPM_* layer/condition GUIDs that fwpmk.h declares). Only one
// compilation unit in the driver should include initguid.h.
#include <initguid.h>
#include <fwpsk.h>
#include <fwpmk.h>
#include <ntstrsafe.h>

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

static HANDLE  g_EngineHandle      = nullptr;
static BOOLEAN g_CalloutV4Registered = FALSE;
static BOOLEAN g_CalloutV6Registered = FALSE;
static UINT32  g_CalloutV4Id       = 0;
static UINT32  g_CalloutV6Id       = 0;
static UINT64  g_FilterV4Id        = 0;
static UINT64  g_FilterV6Id        = 0;

static volatile LONG64 g_NextEventId = 0;

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

static NTSTATUS ScamAlertWriteIpv4Source(UINT32 sourceAddressHostOrder, _Out_writes_(SCAMALERT_MAX_IP_CHARS) wchar_t* sourceIp)
{
    // WFP delivers ALE_AUTH_RECV_ACCEPT_V4 IP_REMOTE_ADDRESS in host byte order.
    return RtlStringCchPrintfW(
        sourceIp,
        SCAMALERT_MAX_IP_CHARS,
        L"%u.%u.%u.%u",
        static_cast<unsigned>((sourceAddressHostOrder >> 24) & 0xff),
        static_cast<unsigned>((sourceAddressHostOrder >> 16) & 0xff),
        static_cast<unsigned>((sourceAddressHostOrder >>  8) & 0xff),
        static_cast<unsigned>( sourceAddressHostOrder        & 0xff));
}

static NTSTATUS ScamAlertWriteIpv6Source(const FWP_BYTE_ARRAY16* sourceAddress, _Out_writes_(SCAMALERT_MAX_IP_CHARS) wchar_t* sourceIp)
{
    if (sourceAddress == nullptr)
    {
        return STATUS_INVALID_PARAMETER;
    }

    const UINT8* b = sourceAddress->byteArray16;
    return RtlStringCchPrintfW(
        sourceIp,
        SCAMALERT_MAX_IP_CHARS,
        L"%02x%02x:%02x%02x:%02x%02x:%02x%02x:%02x%02x:%02x%02x:%02x%02x:%02x%02x",
        b[ 0], b[ 1], b[ 2], b[ 3],
        b[ 4], b[ 5], b[ 6], b[ 7],
        b[ 8], b[ 9], b[10], b[11],
        b[12], b[13], b[14], b[15]);
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

// Tries to pend the classify so the broker can decide. On success, the
// kernel holds the connection in pending state until
// FwpsCompleteClassify0 is called from ScamAlertCompletePendingOp.
//
// Uses the WDK 26100+ async classify pattern:
//   FwpsAcquireClassifyHandle0 -> FwpsPendClassify0 -> ... ->
//   FwpsCompleteClassify0 -> FwpsReleaseClassifyHandle0.
//
// On any failure (allocation, acquire, pend) we leave classifyOut on
// FWP_ACTION_PERMIT and drop the pending entry, so the user is never
// worse off than observe-only.
static VOID ScamAlertPendForBrokerDecision(
    _In_ const SCAMALERT_CONNECTION_EVENT* event,
    _In_ const void*                       classifyContext,
    _In_ const FWPS_FILTER1*               filter,
    _In_ UINT16                            layerId,
    _Inout_ FWPS_CLASSIFY_OUT0*            classifyOut)
{
    UINT64 classifyHandle = 0;
    NTSTATUS status = FwpsAcquireClassifyHandle0(
        const_cast<void*>(classifyContext),
        0,
        &classifyHandle);
    if (!NT_SUCCESS(status) || classifyHandle == 0)
    {
        return; // fail-open: classifyOut stays PERMIT
    }

    status = ScamAlertAddPendingOp(event->EventId, classifyHandle, layerId);
    if (!NT_SUCCESS(status))
    {
        FwpsReleaseClassifyHandle0(classifyHandle);
        return;
    }

    status = FwpsPendClassify0(classifyHandle, filter->filterId, 0, classifyOut);
    if (!NT_SUCCESS(status))
    {
        // Roll back: the pending entry release will run
        // FwpsCompleteClassify0 + Release; that's correct cleanup even
        // though the API call we just made failed - the handle is
        // still acquired, we still owe a Release.
        ScamAlertCompletePendingOp(event->EventId, ScamAlertDecisionAllow);
        // If completion didn't run (e.g., our entry already removed),
        // the leak is bounded; classifyOut was not modified by a
        // failed FwpsPendClassify0, so fail-open behavior holds.
        return;
    }

    // Make the event visible to user mode AFTER pending state is
    // established so the bridge can never call complete on a
    // not-yet-pended handle.
    if (!NT_SUCCESS(ScamAlertQueueConnectionEvent(event)))
    {
        // Best-effort cleanup. The classify is already pending; we
        // complete it ourselves with allow so we don't strand it.
        ScamAlertCompletePendingOp(event->EventId, ScamAlertDecisionAllow);
        return;
    }

    // FwpsPendClassify0 already set classifyOut->actionType to BLOCK,
    // cleared FWPS_RIGHT_ACTION_WRITE, and set ABSORB; nothing more
    // for us to do here.
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
    UNREFERENCED_PARAMETER(filter);
    UNREFERENCED_PARAMETER(flowContext);

    if ((classifyOut->rights & FWPS_RIGHT_ACTION_WRITE) == 0)
    {
        return;
    }

    classifyOut->actionType = FWP_ACTION_PERMIT;

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

    ScamAlertPendForBrokerDecision(&event, classifyContext, filter, FWPS_LAYER_ALE_AUTH_RECV_ACCEPT_V4, classifyOut);
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
    UNREFERENCED_PARAMETER(filter);
    UNREFERENCED_PARAMETER(flowContext);

    if ((classifyOut->rights & FWPS_RIGHT_ACTION_WRITE) == 0)
    {
        return;
    }

    classifyOut->actionType = FWP_ACTION_PERMIT;

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

    ScamAlertPendForBrokerDecision(&event, classifyContext, filter, FWPS_LAYER_ALE_AUTH_RECV_ACCEPT_V6, classifyOut);
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
    NTSTATUS status = ScamAlertRegisterCallouts(DeviceObject);
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
}
