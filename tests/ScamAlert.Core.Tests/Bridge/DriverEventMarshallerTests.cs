using ScamAlert.Contracts;
using ScamAlert.DriverBridge.Driver;

namespace ScamAlert.Core.Tests.Bridge;

public sealed class DriverEventMarshallerTests
{
    [Fact]
    public void ToDriverEvent_translates_native_struct_to_managed_record()
    {
        var eventId = Guid.NewGuid();
        var native = new NativeConnectionEvent
        {
            EventId = eventId.ToByteArray(),
            OccurredAtUnixTimeMilliseconds = 1_770_000_000_000UL,
            SourceIp = "203.0.113.10",
            DestinationPort = 3389,
            ProtectedService = NativeProtectedService.Rdp
        };

        var managed = DriverEventMarshaller.ToDriverEvent(native);

        Assert.Equal(eventId, managed.EventId);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_770_000_000_000L), managed.OccurredAt);
        Assert.Equal("203.0.113.10", managed.SourceIp);
        Assert.Equal(3389, managed.DestinationPort);
        Assert.Equal(ProtectedService.Rdp, managed.ProtectedService);
    }

    [Fact]
    public void ToDriverEvent_throws_for_unknown_protected_service()
    {
        var native = new NativeConnectionEvent
        {
            EventId = Guid.NewGuid().ToByteArray(),
            OccurredAtUnixTimeMilliseconds = 0,
            SourceIp = "203.0.113.10",
            DestinationPort = 12345,
            ProtectedService = (NativeProtectedService)99
        };

        Assert.Throws<InvalidDataException>(() => DriverEventMarshaller.ToDriverEvent(native));
    }

    [Theory]
    [InlineData(DriverDecisionKind.Allow, NativeDriverDecision.Allow)]
    [InlineData(DriverDecisionKind.Block, NativeDriverDecision.Block)]
    public void ToNativeDecision_packs_event_id_and_decision(DriverDecisionKind input, NativeDriverDecision expected)
    {
        var eventId = Guid.NewGuid();
        var decision = DriverEventMarshaller.ToNativeDecision(eventId, input);

        Assert.Equal(eventId.ToByteArray(), decision.EventId);
        Assert.Equal(expected, decision.Decision);
    }
}
