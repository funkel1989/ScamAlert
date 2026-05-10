# Reads the driver's diagnostic counters via IOCTL_SCAMALERT_GET_STATS.
# Run inside the VM (or via Invoke-Command -VMName -FilePath).
#
# Counters tell us where the Milestone B classify/pend path is firing
# vs failing:
#   - ClassifyEntered            inbound TCP attempts that hit our callout
#   - EventsQueued               attempts that were queued for user mode
#   - AcquireOk / AcquireFailed  FwpsAcquireClassifyHandle0 outcome
#   - PendOk / PendFailed        FwpsPendClassify0 outcome
#   - ClassifyContextNull        WFP didn't give us a classifyContext

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
    public const uint IoctlGetStats = 0x80006008;

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
        EventsQueued        = [Runtime.InteropServices.Marshal]::ReadInt64($buf,  8)
        AcquireOk           = [Runtime.InteropServices.Marshal]::ReadInt64($buf, 16)
        AcquireFailed       = [Runtime.InteropServices.Marshal]::ReadInt64($buf, 24)
        PendOk              = [Runtime.InteropServices.Marshal]::ReadInt64($buf, 32)
        PendFailed          = [Runtime.InteropServices.Marshal]::ReadInt64($buf, 40)
        ClassifyContextNull = [Runtime.InteropServices.Marshal]::ReadInt64($buf, 48)
    } | Format-List
}
finally {
    [Runtime.InteropServices.Marshal]::FreeHGlobal($buf)
    $handle.Dispose()
}
