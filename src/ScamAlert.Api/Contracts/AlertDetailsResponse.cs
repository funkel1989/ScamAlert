using ScamAlert.Data.Enums;

namespace ScamAlert.Api.Contracts;

public sealed record AlertDetailsResponse(
    Guid AlertId,
    Guid CustomerId,
    Guid DeviceId,
    string SourceIp,
    int DestinationPort,
    string Service,
    string? DestinationIp,
    string? Transport,
    string? Direction,
    string? ObservedBy,
    string? RuleApplied,
    string? DecisionReason,
    string? Notes,
    AlertResolutionStatus ResolutionStatus,
    Guid? AcknowledgedByContactId,
    DateTimeOffset CreatedUtc,
    List<NotificationAttemptResponse> Attempts);

public sealed record NotificationAttemptResponse(
    Guid ContactId,
    string ContactName,
    int EscalationOrder,
    string Channel,
    NotificationOutcome Outcome,
    string? ProviderMessageId,
    DateTimeOffset AttemptedUtc);

public sealed record AlertSummaryResponse(
    Guid AlertId,
    Guid CustomerId,
    Guid DeviceId,
    string SourceIp,
    int DestinationPort,
    string Service,
    string? DestinationIp,
    string? Transport,
    string? Direction,
    string? ObservedBy,
    string? RuleApplied,
    string? DecisionReason,
    AlertResolutionStatus ResolutionStatus,
    Guid? AcknowledgedByContactId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

public sealed record AlertActionRequest(
    [System.ComponentModel.DataAnnotations.StringLength(500)] string? Notes,
    Guid? ContactId);
