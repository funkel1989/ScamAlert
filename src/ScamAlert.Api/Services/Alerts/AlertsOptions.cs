namespace ScamAlert.Api.Services.Alerts;

public sealed class AlertsOptions
{
    public const string SectionName = "Alerts";

    /// <summary>Seconds to wait after a no-response notification before notifying the next escalation tier.</summary>
    public int EscalationDelaySeconds { get; set; } = 120;

    /// <summary>How often the escalation worker scans for due alerts.</summary>
    public int EscalationPollIntervalSeconds { get; set; } = 30;
}
