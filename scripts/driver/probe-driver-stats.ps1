# Reads the driver's diagnostic counters via IOCTL_SCAMALERT_GET_STATS.
# Run inside the VM (or via Invoke-Command -VMName -FilePath).
#
# Counters tell us where the Milestone B clone-and-reinject pend path is
# firing vs failing:
#   - ClassifyEntered      callout hits (includes self-injected reinjections)
#   - SelfInjectedSkipped  recognized our own reinjection -> instant PERMIT
#   - EventsQueued         attempts placed on the user-mode event queue
#   - PendOk               FwpsPendOperation0 + state insert succeeded
#   - AllowInjected        ALLOW path: clone+reinject succeeded
#   - BlockReleased        BLOCK path: FwpsCompleteOperation0(ctx, NULL)
#   - TimedOutFailOpen     30s kernel timeout fired -> fail-open

[CmdletBinding()]
param(
    [string]$DevicePath = '\\.\ScamAlertWfp'
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class ScamAlertStatsProbe
{
    // CTL_CODE(0x8000, 0x803, METHOD_BUFFERED, FILE_READ_DATA)
    //   = (0x8000 << 16) | (FILE_READ_DATA << 14) | (0x803 << 2) | METHOD_BUFFERED
    //   = 0x80000000 | 0x00004000 | 0x0000200C | 0 = 0x8000600C
    public const uint IoctlGetStats = 0x8000600C;

    public const uint GenericRead       = 0x80000000;
    public const uint GenericWrite      = 0x40000000;
    public const uint FileShareRead     = 0x00000001;
    public const uint FileShareWrite    = 0x00000002;
    public const uint OpenExisting      = 3;
    public const uint FileAttributeNormal = 0x80;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    public const int StatsStructSize = 56; // 7 * sizeof(uint64_t)
}
"@

$handle = [ScamAlertStatsProbe]::CreateFileW(
    $DevicePath,
    [ScamAlertStatsProbe]::GenericRead -bor [ScamAlertStatsProbe]::GenericWrite,
    [ScamAlertStatsProbe]::FileShareRead -bor [ScamAlertStatsProbe]::FileShareWrite,
    [IntPtr]::Zero,
    [ScamAlertStatsProbe]::OpenExisting,
    [ScamAlertStatsProbe]::FileAttributeNormal,
    [IntPtr]::Zero)

if ($handle.IsInvalid) {
    $err = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    throw "CreateFile('$DevicePath') failed (Win32 $err). Is the driver loaded?"
}

$buf = [Runtime.InteropServices.Marshal]::AllocHGlobal([ScamAlertStatsProbe]::StatsStructSize)
try {
    $bytesReturned = 0
    $ok = [ScamAlertStatsProbe]::DeviceIoControl(
        $handle,
        [ScamAlertStatsProbe]::IoctlGetStats,
        [IntPtr]::Zero, 0,
        $buf, [uint32][ScamAlertStatsProbe]::StatsStructSize,
        [ref]$bytesReturned,
        [IntPtr]::Zero)
    if (-not $ok) {
        $err = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        throw "DeviceIoControl(IOCTL_SCAMALERT_GET_STATS) failed (Win32 $err)."
    }

    [pscustomobject]@{
        ClassifyEntered     = [Runtime.InteropServices.Marshal]::ReadInt64($buf,  0)
        SelfInjectedSkipped = [Runtime.InteropServices.Marshal]::ReadInt64($buf,  8)
        EventsQueued        = [Runtime.InteropServices.Marshal]::ReadInt64($buf, 16)
        PendOk              = [Runtime.InteropServices.Marshal]::ReadInt64($buf, 24)
        AllowInjected       = [Runtime.InteropServices.Marshal]::ReadInt64($buf, 32)
        BlockReleased       = [Runtime.InteropServices.Marshal]::ReadInt64($buf, 40)
        TimedOutFailOpen    = [Runtime.InteropServices.Marshal]::ReadInt64($buf, 48)
    } | Format-List
}
finally {
    [Runtime.InteropServices.Marshal]::FreeHGlobal($buf)
    $handle.Dispose()
}
