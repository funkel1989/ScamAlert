namespace ScamAlert.DriverBridge.Configuration;

public sealed class DriverBridgeOptions
{
    public const string SectionName = "DriverBridge";

    public string DevicePath { get; set; } = @"\\.\ScamAlertWfp";

    public string BrokerPipeName { get; set; } = "scamalert-driver-events";

    public int BrokerConnectionTimeoutSeconds { get; set; } = 3;

    public int BrokerRequestTimeoutSeconds { get; set; } = 30;

    public int DeviceOpenRetryDelaySeconds { get; set; } = 5;
}
