using System.Runtime.InteropServices;

namespace ScamAlert.DriverBridge.Driver;

// Mirror of native/ScamAlert.Driver.Shared/ScamAlertDriverIoctl.h.
// Sizes are asserted by NativeDriverContractsTests so the wire format
// stays in sync between kernel and user mode.

public enum NativeProtectedService : uint
{
    Rdp    = 1,
    Ssh    = 2,
    Telnet = 3
}

public enum NativeDriverDecision : uint
{
    Allow = 1,
    Block = 2
}

public static class NativeDriverIoctl
{
    public const uint DeviceType = 0x8000;

    // CTL_CODE(DeviceType, Function, Method, Access) packing:
    // (DeviceType << 16) | (Access << 14) | (Function << 2) | Method
    private const uint MethodBuffered = 0;
    private const uint FileReadData   = 0x0001;
    private const uint FileWriteData  = 0x0002;

    public static readonly uint IoctlGetEvent =
        (DeviceType << 16) | (FileReadData << 14) | (0x801u << 2) | MethodBuffered;

    public static readonly uint IoctlCompleteEvent =
        (DeviceType << 16) | (FileWriteData << 14) | (0x802u << 2) | MethodBuffered;

    public const int MaxIpChars = 46;
}

[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
public struct NativeConnectionEvent
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] EventId;

    public ulong OccurredAtUnixTimeMilliseconds;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = NativeDriverIoctl.MaxIpChars)]
    public string SourceIp;

    public ushort DestinationPort;

    public NativeProtectedService ProtectedService;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct NativeConnectionDecision
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] EventId;

    public NativeDriverDecision Decision;
}
