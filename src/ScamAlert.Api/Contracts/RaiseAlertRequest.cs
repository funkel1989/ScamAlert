using System.ComponentModel.DataAnnotations;

namespace ScamAlert.Api.Contracts;

public sealed record RaiseAlertRequest(
    [property: Required, StringLength(256)] string ExternalDeviceId,
    [property: Required, StringLength(45)] string SourceIp,
    [property: Range(1, 65535)] int DestinationPort,
    [property: Required, StringLength(256)] string Service,
    int? SimulateAcknowledgeAtEscalationOrder);
