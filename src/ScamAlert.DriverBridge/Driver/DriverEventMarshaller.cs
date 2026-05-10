using ScamAlert.Contracts;

namespace ScamAlert.DriverBridge.Driver;

// Translates between SCAMALERT_CONNECTION_EVENT / DECISION wire structs
// and the broker-pipe contracts. Pulled out of DriverDeviceClient so the
// translation rules are unit-testable without touching DeviceIoControl.
public static class DriverEventMarshaller
{
    public static DriverEvent ToDriverEvent(NativeConnectionEvent native)
    {
        var eventId = new Guid(native.EventId);
        var occurredAt = DateTimeOffset.FromUnixTimeMilliseconds((long)native.OccurredAtUnixTimeMilliseconds);
        var service = native.ProtectedService switch
        {
            NativeProtectedService.Rdp    => ProtectedService.Rdp,
            NativeProtectedService.Ssh    => ProtectedService.Ssh,
            NativeProtectedService.Telnet => ProtectedService.Telnet,
            _ => throw new InvalidDataException(
                $"Driver event has unknown protected-service value {(uint)native.ProtectedService}.")
        };

        return new DriverEvent(
            eventId,
            occurredAt,
            native.SourceIp ?? string.Empty,
            native.DestinationPort,
            service);
    }

    public static NativeConnectionDecision ToNativeDecision(Guid eventId, DriverDecisionKind decision)
    {
        return new NativeConnectionDecision
        {
            EventId  = eventId.ToByteArray(),
            Decision = decision switch
            {
                DriverDecisionKind.Allow => NativeDriverDecision.Allow,
                DriverDecisionKind.Block => NativeDriverDecision.Block,
                _ => throw new ArgumentOutOfRangeException(nameof(decision))
            }
        };
    }
}
