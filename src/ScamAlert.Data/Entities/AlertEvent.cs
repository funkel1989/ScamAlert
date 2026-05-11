using ScamAlert.Data.Enums;

namespace ScamAlert.Data.Entities;

public sealed class AlertEvent
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid DeviceId { get; set; }
    public string SourceIp { get; set; } = string.Empty;
    public int DestinationPort { get; set; }
    public string Service { get; set; } = string.Empty;
    public string? DestinationIp { get; set; }
    public string? Transport { get; set; }
    public string? Direction { get; set; }
    public string? ObservedBy { get; set; }
    public string? RuleApplied { get; set; }
    public string? DecisionReason { get; set; }
    public string? Notes { get; set; }
    /// <summary>Optional id from the client (e.g. broker attempt event id) for idempotent alert creation.</summary>
    public Guid? ClientEventId { get; set; }
    public AlertResolutionStatus ResolutionStatus { get; set; }
    public Guid? AcknowledgedByContactId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    public Customer Customer { get; set; } = null!;
    public MonitoredDevice Device { get; set; } = null!;
    public List<NotificationAttempt> NotificationAttempts { get; set; } = [];
}
