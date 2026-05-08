namespace ScamAlert.Core.CloudAlerts;

public sealed class CloudAlertOutboxItem
{
    public Guid Id { get; set; }

    public Guid ClientEventId { get; set; }

    public required string ExternalDeviceId { get; set; }

    public required string SourceIp { get; set; }

    public int DestinationPort { get; set; }

    public required string Service { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset NextAttemptUtc { get; set; }

    public DateTimeOffset EnqueuedUtc { get; set; }

    public string? LastError { get; set; }
}
