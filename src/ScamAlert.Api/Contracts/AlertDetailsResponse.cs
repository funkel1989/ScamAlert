using ScamAlert.Data.Enums;

namespace ScamAlert.Api.Contracts;

public sealed record AlertDetailsResponse(
    Guid AlertId,
    Guid CustomerId,
    Guid DeviceId,
    string SourceIp,
    int DestinationPort,
    string Service,
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
