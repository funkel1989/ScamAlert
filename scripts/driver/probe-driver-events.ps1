# Pure-PowerShell IOCTL probe against \\.\ScamAlertWfp. Pulls events
# off the kernel queue and prints them to stdout so we can confirm the
# WFP callouts are firing without needing to publish the .NET bridge.
#
# Run inside the VM (not on the host - the device only exists where the
# driver is loaded). Or run it from the host via:
#
#   $cred = Get-Credential -UserName dev
#   Invoke-Command -VMName ScamAlertDev -Credential $cred -FilePath scripts/driver/probe-driver-events.ps1
#
# The script polls every 250 ms, prints a row per event, and exits on
# Ctrl+C. While it's running, generate an inbound TCP attempt to one
# of the protected ports (22, 23, 3389) from another machine.

[CmdletBinding()]
param(
    [string]$DevicePath = '\\.\ScamAlertWfp',
    [int]$IntervalMilliseconds = 250
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class ScamAlertProbe
{
    public const uint IoctlGetEvent = 0x80006004;

    public const uint GenericRead       = 0x80000000;
    public const uint GenericWrite      = 0x40000000;
    public const uint FileShareRead     = 0x00000001;
    public const uint FileShareWrite    = 0x00000002;
    public const uint OpenExisting      = 3;
    public const uint FileAttributeNormal = 0x80;
    public const int  ErrorNoMoreItems  = 259;

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

    public const int  EventStructSize = 122;
    public const int  IpFieldSize     = 92;     // 46 wchars
    public const int  IpFieldOffset   = 24;
    public const int  PortOffset      = 116;
    public const int  ServiceOffset   = 118;
    public const int  EventIdOffset   = 0;
    public const int  TimeOffset      = 16;
}
"@

$handle = [ScamAlertProbe]::CreateFileW(
    $DevicePath,
    [ScamAlertProbe]::GenericRead -bor [ScamAlertProbe]::GenericWrite,
    [ScamAlertProbe]::FileShareRead -bor [ScamAlertProbe]::FileShareWrite,
    [IntPtr]::Zero,
    [ScamAlertProbe]::OpenExisting,
    [ScamAlertProbe]::FileAttributeNormal,
    [IntPtr]::Zero)

if ($handle.IsInvalid) {
    $err = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    throw "CreateFile('$DevicePath') failed (Win32 $err). Is the driver loaded? sc query ScamAlertWfp"
}

Write-Host "Connected to $DevicePath. Polling every $IntervalMilliseconds ms. Press Ctrl+C to stop." -ForegroundColor Cyan
Write-Host ""
Write-Host ("{0,-20} {1,-38} {2,-7} {3,-8} {4}" -f 'OccurredAt(UTC)', 'EventId', 'Port', 'Service', 'SourceIp')
Write-Host ("{0,-20} {1,-38} {2,-7} {3,-8} {4}" -f ('-'*19), ('-'*36), ('-'*5), ('-'*7), ('-'*15))

$bufferSize = [ScamAlertProbe]::EventStructSize
$buffer     = [Runtime.InteropServices.Marshal]::AllocHGlobal($bufferSize)

try {
    while ($true) {
        for ($i = 0; $i -lt $bufferSize; $i++) {
            [Runtime.InteropServices.Marshal]::WriteByte($buffer, $i, 0)
        }

        $bytesReturned = 0
        $ok = [ScamAlertProbe]::DeviceIoControl(
            $handle,
            [ScamAlertProbe]::IoctlGetEvent,
            [IntPtr]::Zero, 0,
            $buffer, [uint32]$bufferSize,
            [ref]$bytesReturned,
            [IntPtr]::Zero)

        if (-not $ok) {
            $err = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
            if ($err -eq [ScamAlertProbe]::ErrorNoMoreItems) {
                Start-Sleep -Milliseconds $IntervalMilliseconds
                continue
            }
            Write-Warning "DeviceIoControl failed (Win32 $err). Stopping."
            break
        }

        # Decode SCAMALERT_CONNECTION_EVENT (Pack=1)
        $eventIdBytes = New-Object byte[] 16
        for ($i = 0; $i -lt 16; $i++) {
            $eventIdBytes[$i] = [Runtime.InteropServices.Marshal]::ReadByte($buffer, [ScamAlertProbe]::EventIdOffset + $i)
        }
        $eventId = [Guid]::new($eventIdBytes)

        $unixMs = [Runtime.InteropServices.Marshal]::ReadInt64($buffer, [ScamAlertProbe]::TimeOffset)
        $occurredAt = [DateTimeOffset]::FromUnixTimeMilliseconds($unixMs).UtcDateTime

        $ipPtr = [IntPtr]::Add($buffer, [ScamAlertProbe]::IpFieldOffset)
        $sourceIp = [Runtime.InteropServices.Marshal]::PtrToStringUni($ipPtr, 46).TrimEnd([char]0)

        $port    = [Runtime.InteropServices.Marshal]::ReadInt16($buffer, [ScamAlertProbe]::PortOffset)  -band 0xFFFF
        $service = [Runtime.InteropServices.Marshal]::ReadInt32($buffer, [ScamAlertProbe]::ServiceOffset)
        $serviceName = switch ($service) { 1 {'rdp'} 2 {'ssh'} 3 {'telnet'} default {"#$service"} }

        Write-Host ("{0,-20} {1,-38} {2,-7} {3,-8} {4}" -f
            ($occurredAt.ToString('HH:mm:ss.fff')),
            $eventId.ToString('D'),
            $port,
            $serviceName,
            $sourceIp) -ForegroundColor Green
    }
}
finally {
    [Runtime.InteropServices.Marshal]::FreeHGlobal($buffer)
    $handle.Dispose()
}
