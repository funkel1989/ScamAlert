namespace ScamAlert.Core.CloudAlerts;

public readonly record struct CloudAlertEnqueueSnapshot(
    bool Enabled,
    string ExternalDeviceId,
    TimeSpan DedupeWindow);

public interface ICloudAlertEnqueueSource
{
    CloudAlertEnqueueSnapshot GetSnapshot();
}
