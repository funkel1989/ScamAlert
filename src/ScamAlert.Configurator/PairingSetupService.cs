using System.Net.Http.Json;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Win32;

namespace ScamAlert.Configurator;

internal sealed class PairingSetupService(HttpClient httpClient)
{
    public static string? ReadDefaultApiBaseUrlFromRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(BrokerConfigPaths.DefaultApiBaseUrlRegistryKey);
            var value = key?.GetValue(BrokerConfigPaths.DefaultApiBaseUrlRegistryValue) as string;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch
        {
            return null;
        }
    }

    public async Task<PairingRedeemResult> RedeemAsync(string apiBaseUrl, string pairingCode, CancellationToken cancellationToken)
    {
        var baseUrl = apiBaseUrl.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Enter your ScamAlert website address (for example https://app.scamalert.com).");
        }

        var code = pairingCode.Trim().ToUpperInvariant();
        if (code.Length is < 6 or > 12)
        {
            throw new InvalidOperationException("Enter the 8-character pairing code from the portal Devices page.");
        }

        var uri = $"{baseUrl}/api/setup/redeem";
        using var response = await httpClient.PostAsJsonAsync(uri, new { code }, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                response.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? "That pairing code is invalid or expired. Generate a new code from Devices → Pair PC."
                    : $"Could not reach ScamAlert ({(int)response.StatusCode}). Check the website address and your internet connection.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        return new PairingRedeemResult(
            GetString(root, "apiBaseUrl") ?? baseUrl,
            GetRequiredString(root, "externalDeviceId"),
            GetRequiredString(root, "deviceIngestApiKey"),
            GetString(root, "deviceName") ?? "This PC");
    }

    public static void TryRestartBrokerService(out string? userMessage)
    {
        userMessage = null;
        try
        {
            using var service = new ServiceController(BrokerConfigPaths.BrokerServiceName);
            if (service.Status == ServiceControllerStatus.Running)
            {
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }

            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
        }
        catch (InvalidOperationException)
        {
            userMessage = "Configuration saved. Start the \"ScamAlert Broker\" service in Services (services.msc) if alerts do not connect.";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            userMessage = "Configuration saved. Restart the \"ScamAlert Broker\" service as an administrator, or restart this computer.";
        }
    }

    private static string? GetString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) ? value.GetString() : null;

    private static string GetRequiredString(JsonElement root, string property)
    {
        var text = GetString(root, property);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("The server returned an incomplete pairing response.");
        }

        return text;
    }
}
