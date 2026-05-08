using System.ComponentModel.DataAnnotations;

namespace ScamAlert.Api.Contracts;

public sealed record RaiseAlertRequest(
    [Required, StringLength(256)] string ExternalDeviceId,
    [Required, StringLength(45)] string SourceIp,
    [Range(1, 65535)] int DestinationPort,
    [Required, StringLength(256)] string Service,
    int? SimulateAcknowledgeAtEscalationOrder,
    Guid? ClientEventId);
