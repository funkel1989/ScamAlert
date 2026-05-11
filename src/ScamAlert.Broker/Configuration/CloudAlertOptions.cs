namespace ScamAlert.Broker.Configuration;

public sealed class CloudAlertOptions
{
    public const string SectionName = "CloudAlerts";

    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "http://localhost:5000";

    public string ExternalDeviceId { get; set; } = string.Empty;

    public string DeviceIngestApiKey { get; set; } = string.Empty;

    public int DedupeWindowSeconds { get; set; } = 600;

    public int MaxDeliveryAttempts { get; set; } = 8;

    public int InitialRetryDelaySeconds { get; set; } = 2;

    public int MaxRetryDelaySeconds { get; set; } = 300;

    public int PollIntervalSeconds { get; set; } = 5;
}
