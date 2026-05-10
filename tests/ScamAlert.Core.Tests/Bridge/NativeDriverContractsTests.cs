using System.Runtime.InteropServices;
using ScamAlert.DriverBridge.Driver;

namespace ScamAlert.Core.Tests.Bridge;

public sealed class NativeDriverContractsTests
{
    [Fact]
    public void NativeConnectionEvent_size_matches_native_layout()
    {
        // 16 (EventId) + 8 (OccurredAt) + 92 (SourceIp WCHAR[46]) + 2 (DestPort) + 4 (Service) = 122
        Assert.Equal(122, Marshal.SizeOf<NativeConnectionEvent>());
    }

    [Fact]
    public void NativeConnectionDecision_size_matches_native_layout()
    {
        // 16 (EventId) + 4 (Decision) = 20
        Assert.Equal(20, Marshal.SizeOf<NativeConnectionDecision>());
    }

    [Fact]
    public void NativeProtectedService_values_match_native_enum()
    {
        Assert.Equal(1u, (uint)NativeProtectedService.Rdp);
        Assert.Equal(2u, (uint)NativeProtectedService.Ssh);
        Assert.Equal(3u, (uint)NativeProtectedService.Telnet);
    }

    [Fact]
    public void NativeDriverDecision_values_match_native_enum()
    {
        Assert.Equal(1u, (uint)NativeDriverDecision.Allow);
        Assert.Equal(2u, (uint)NativeDriverDecision.Block);
    }

    [Fact]
    public void Ioctl_codes_match_CTL_CODE_packing()
    {
        // CTL_CODE(0x8000, 0x801, METHOD_BUFFERED, FILE_READ_DATA)
        //   = (0x8000 << 16) | (FILE_READ_DATA << 14) | (0x801 << 2) | METHOD_BUFFERED
        //   = 0x80002004 | 0x00004000 = 0x80006004
        Assert.Equal(0x80006004u, NativeDriverIoctl.IoctlGetEvent);

        // CTL_CODE(0x8000, 0x802, METHOD_BUFFERED, FILE_WRITE_DATA)
        //   = (0x8000 << 16) | (FILE_WRITE_DATA << 14) | (0x802 << 2) | METHOD_BUFFERED
        //   = 0x80002008 | 0x00008000 = 0x8000A008
        Assert.Equal(0x8000A008u, NativeDriverIoctl.IoctlCompleteEvent);
    }
}
