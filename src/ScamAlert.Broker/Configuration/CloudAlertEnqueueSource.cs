using Microsoft.Extensions.Options;
using ScamAlert.Core.CloudAlerts;

namespace ScamAlert.Broker.Configuration;

public sealed class CloudAlertEnqueueSource(IOptionsMonitor<CloudAlertOptions> options) : ICloudAlertEnqueueSource
{
    public CloudAlertEnqueueSnapshot GetSnapshot()
    {
        var o = options.CurrentValue;
        return new CloudAlertEnqueueSnapshot(
            o.Enabled,
            o.ExternalDeviceId,
            TimeSpan.FromSeconds(Math.Max(1, o.DedupeWindowSeconds)));
    }
}
