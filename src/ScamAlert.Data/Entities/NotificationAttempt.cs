using ScamAlert.Data.Enums;

namespace ScamAlert.Data.Entities;

public sealed class NotificationAttempt
{
    public Guid Id { get; set; }
    public Guid AlertEventId { get; set; }
    public Guid ContactId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public NotificationOutcome Outcome { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? AcknowledgmentToken { get; set; }
    public DateTimeOffset? AcknowledgedUtc { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset AttemptedUtc { get; set; }

    public AlertEvent AlertEvent { get; set; } = null!;
    public Contact Contact { get; set; } = null!;
}
