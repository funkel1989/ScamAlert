using System.ComponentModel.DataAnnotations;

namespace ScamAlert.Api.Contracts;

public sealed record RaiseAlertRequest(
    [Required, StringLength(256)] string ExternalDeviceId,
    [Required, StringLength(45)] string SourceIp,
    [Range(1, 65535)] int DestinationPort,
    [Required, StringLength(256)] string Service,
    [StringLength(45)] string? DestinationIp,
    [StringLength(16)] string? Transport,
    [StringLength(16)] string? Direction,
    [StringLength(64)] string? ObservedBy,
    [StringLength(128)] string? RuleApplied,
    [StringLength(128)] string? DecisionReason,
    [StringLength(500)] string? Notes,
    int? SimulateAcknowledgeAtEscalationOrder,
    Guid? ClientEventId);
