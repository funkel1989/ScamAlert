using System.Text.Json;
using ScamAlert.Contracts;

namespace ScamAlert.Core.Tests.Contracts;

public sealed class RemoteAccessContractsTests
{
    [Theory]
    [InlineData(3389, ProtectedService.Rdp)]
    [InlineData(22, ProtectedService.Ssh)]
    [InlineData(23, ProtectedService.Telnet)]
    public void TryFromPortMapsProtectedPorts(int destinationPort, ProtectedService expectedService)
    {
        var mapped = ProtectedServiceMap.TryFromPort(destinationPort, out var service);

        Assert.True(mapped);
        Assert.Equal(expectedService, service);
    }

    [Fact]
    public void TryFromPortRejectsUnprotectedPorts()
    {
        var mapped = ProtectedServiceMap.TryFromPort(443, out var service);

        Assert.False(mapped);
        Assert.Equal(default, service);
    }

    [Fact]
    public void ObservedInboundAttemptSignalSerializesWithCamelCaseAndStringEnums()
    {
        var signal = new ObservedInboundAttemptSignal(
            EventId: Guid.Parse("a9cd41a2-38ed-45ba-b743-7cf4fa47f943"),
            OccurredAt: DateTimeOffset.Parse("2026-05-06T12:34:56Z"),
            SourceIp: "203.0.113.7",
            DestinationPort: 3389,
            ProtectedService: ProtectedService.Rdp,
            LocalPolicyMode: TimeoutPolicy.AllowOnTimeout,
            DecisionStatus: DecisionStatus.Pending);

        var json = JsonSerializer.Serialize(signal, SignalJson.Options);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("eventType", out var eventType));
        Assert.Equal("ObservedInboundAttempt", eventType.GetString());
        Assert.True(root.TryGetProperty("sourceIp", out var sourceIp));
        Assert.Equal("203.0.113.7", sourceIp.GetString());
        Assert.True(root.TryGetProperty("protectedService", out var protectedService));
        Assert.Equal("rdp", protectedService.GetString());
        Assert.False(root.TryGetProperty("EventType", out _));
        Assert.False(root.TryGetProperty("SourceIp", out _));
        Assert.False(root.TryGetProperty("ProtectedService", out _));
    }
}
