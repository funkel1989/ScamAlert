using ScamAlert.Contracts;

namespace ScamAlert.DriverBridge.Driver;

// Managed projection of SCAMALERT_CONNECTION_EVENT from the driver,
// pre-mapped to the broker-pipe vocabulary so the worker can hand
// it straight to BrokerDriverPipeClient.
public sealed record DriverEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string SourceIp,
    int DestinationPort,
    ProtectedService ProtectedService);
