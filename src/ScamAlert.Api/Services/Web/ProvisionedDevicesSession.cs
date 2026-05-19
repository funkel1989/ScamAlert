using System.Text.Json;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using ScamAlert.Api.Contracts;

namespace ScamAlert.Api.Services.Web;

public sealed class ProvisionedDevicesSession(ProtectedSessionStorage sessionStorage)
{
    private const string StorageKey = "scamalert.provisionedDevices";

    public async Task SaveAsync(IReadOnlyList<ProvisionedDeviceResponse> devices)
    {
        var json = JsonSerializer.Serialize(devices);
        await sessionStorage.SetAsync(StorageKey, json);
    }

    public async Task<IReadOnlyList<ProvisionedDeviceResponse>?> LoadAndClearAsync()
    {
        var result = await sessionStorage.GetAsync<string>(StorageKey);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Value))
        {
            return null;
        }

        await sessionStorage.DeleteAsync(StorageKey);
        return JsonSerializer.Deserialize<List<ProvisionedDeviceResponse>>(result.Value);
    }
}
