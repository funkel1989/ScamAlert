using Microsoft.EntityFrameworkCore;
using ScamAlert.Data;

namespace ScamAlert.Api.Services.Auth;

public interface IDeviceIngestAuthService
{
    Task<bool> IsAuthorizedAsync(string externalDeviceId, string providedKey, CancellationToken cancellationToken);
}

public sealed class DeviceIngestAuthService(
    ScamAlertDbContext dbContext,
    IPasswordHasher passwordHasher) : IDeviceIngestAuthService
{
    public async Task<bool> IsAuthorizedAsync(string externalDeviceId, string providedKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(externalDeviceId) || string.IsNullOrWhiteSpace(providedKey))
        {
            return false;
        }

        var device = await dbContext.Devices
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.ExternalDeviceId == externalDeviceId && x.IsActive, cancellationToken);
        if (device is null || string.IsNullOrWhiteSpace(device.IngestApiKeyHash))
        {
            return false;
        }

        return passwordHasher.Verify(device.IngestApiKeyHash, providedKey);
    }
}
