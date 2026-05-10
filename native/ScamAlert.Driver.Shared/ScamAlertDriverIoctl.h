#pragma once

// Shared C-compatible binary contract between the ScamAlert WFP kernel
// driver and the user-mode DriverBridge. Consumers must include the
// platform header that defines CTL_CODE before including this file:
//   - kernel mode: <ntddk.h>
//   - user mode:   <winioctl.h>
//
// All structures use 1-byte packing and fixed-size little-endian fields
// so the same wire format is correct from kernel mode and from C# via
// [StructLayout(LayoutKind.Sequential, Pack = 1)].

#ifdef _KERNEL_MODE
// Kernel mode: <ntddk.h> provides UINT8/UINT16/UINT32/UINT64 already.
// Including <stdint.h> here pulls user-mode CRT into ring 0 and breaks
// the build, so alias the stdint names to the kernel-mode equivalents.
typedef UINT8  uint8_t;
typedef UINT16 uint16_t;
typedef UINT32 uint32_t;
typedef UINT64 uint64_t;
#else
#include <stdint.h>
#endif

#define SCAMALERT_DEVICE_TYPE 0x8000

#define IOCTL_SCAMALERT_GET_EVENT \
    CTL_CODE(SCAMALERT_DEVICE_TYPE, 0x801, METHOD_BUFFERED, FILE_READ_DATA)

#define IOCTL_SCAMALERT_COMPLETE_EVENT \
    CTL_CODE(SCAMALERT_DEVICE_TYPE, 0x802, METHOD_BUFFERED, FILE_WRITE_DATA)

#define IOCTL_SCAMALERT_GET_STATS \
    CTL_CODE(SCAMALERT_DEVICE_TYPE, 0x803, METHOD_BUFFERED, FILE_READ_DATA)

// Max IPv6 textual length (45 chars: "ffff:ffff:ffff:ffff:ffff:ffff:255.255.255.255")
// plus the null terminator. WCHAR (UTF-16) on Windows.
#define SCAMALERT_MAX_IP_CHARS 46

typedef enum SCAMALERT_PROTECTED_SERVICE
{
    ScamAlertServiceRdp    = 1,
    ScamAlertServiceSsh    = 2,
    ScamAlertServiceTelnet = 3
} SCAMALERT_PROTECTED_SERVICE;

typedef enum SCAMALERT_DRIVER_DECISION
{
    ScamAlertDecisionAllow = 1,
    ScamAlertDecisionBlock = 2
} SCAMALERT_DRIVER_DECISION;

#pragma pack(push, 1)

typedef struct SCAMALERT_CONNECTION_EVENT
{
    uint8_t  EventId[16];
    uint64_t OccurredAtUnixTimeMilliseconds;
    uint16_t SourceIp[SCAMALERT_MAX_IP_CHARS]; // UTF-16 null-terminated
    uint16_t DestinationPort;
    uint32_t ProtectedService;                 // SCAMALERT_PROTECTED_SERVICE
} SCAMALERT_CONNECTION_EVENT;

typedef struct SCAMALERT_CONNECTION_DECISION
{
    uint8_t  EventId[16];
    uint32_t Decision;                         // SCAMALERT_DRIVER_DECISION
} SCAMALERT_CONNECTION_DECISION;

// Diagnostic counters returned by IOCTL_SCAMALERT_GET_STATS.
//
// The verdict-delivery model at FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V{4,6} is
// "pend-and-reinject", not the reauth-driven model used elsewhere in WFP:
//   * Allow = clone the NBL, FwpsCompleteOperation0(ctx, clone), then
//             FwpsInjectTransportReceiveAsync0 the clone back into the stack;
//             our classifyFn sees the clone again as a self-injected packet
//             and short-circuits to PERMIT.
//   * Block = FwpsCompleteOperation0(ctx, NULL) and drop the saved NBL
//             reference; the original packet stays blocked from the
//             classifyOut we set in the original classify hit.
typedef struct SCAMALERT_DRIVER_STATS
{
    uint64_t ClassifyEntered;       // total entries into classify (incl. self-injected hits)
    uint64_t SelfInjectedSkipped;   // hits we recognized as our own reinjection -> instant PERMIT
    uint64_t EventsQueued;          // events placed on user-mode queue
    uint64_t PendOk;                // FwpsPendOperation0 + state insert succeeded
    uint64_t AllowInjected;         // FwpsInjectTransportReceiveAsync0 succeeded for ALLOW path
    uint64_t BlockReleased;         // FwpsCompleteOperation0(ctx, NULL) issued for BLOCK path
    uint64_t TimedOutFailOpen;      // 30s kernel timeout fired -> fail-open inject
} SCAMALERT_DRIVER_STATS;

#pragma pack(pop)

// Sizes are part of the wire format. The C# side asserts these with
// Marshal.SizeOf<NativeConnectionEvent>() and SizeOf<NativeConnectionDecision>().
//   SCAMALERT_CONNECTION_EVENT    = 16 + 8 + 92 + 2 + 4 = 122 bytes
//   SCAMALERT_CONNECTION_DECISION = 16 + 4               = 20  bytes
