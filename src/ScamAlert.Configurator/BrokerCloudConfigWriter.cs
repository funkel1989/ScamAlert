using System.Text.Json;

namespace ScamAlert.Configurator;

internal static class BrokerCloudConfigWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static void Write(PairingRedeemResult redeem)
    {
        Directory.CreateDirectory(BrokerConfigPaths.ProgramDataDirectory);

        var payload = new
        {
            CloudAlerts = new
            {
                Enabled = true,
                BaseUrl = redeem.ApiBaseUrl.TrimEnd('/'),
                ExternalDeviceId = redeem.ExternalDeviceId,
                DeviceIngestApiKey = redeem.DeviceIngestApiKey
            }
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        File.WriteAllText(BrokerConfigPaths.BrokerAppSettingsFile, json);
    }
}
