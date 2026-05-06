namespace ScamAlert.Api.Contracts;

public sealed record RaiseAlertRequest(
    string ExternalDeviceId,
    string SourceIp,
    int DestinationPort,
    string Service,
    int? SimulateAcknowledgeAtEscalationOrder);
