using Microsoft.EntityFrameworkCore;
using ScamAlert.Api.Contracts;
using ScamAlert.Api.Services.Auth;
using ScamAlert.Data;
using ScamAlert.Data.Entities;

namespace ScamAlert.Api.Services.Portal;

public sealed class PortalDeviceService(
    ScamAlertDbContext dbContext,
    IPasswordHasher passwordHasher) : IPortalDeviceService
{
    public async Task<IReadOnlyList<PortalDeviceResponse>> ListAsync(Guid customerId, CancellationToken cancellationToken)
    {
        return await dbContext.Devices.AsNoTracking()
            .Where(x => x.CustomerId == customerId)
            .OrderBy(x => x.DeviceName)
            .Select(x => new PortalDeviceResponse(
                x.Id,
                x.DeviceName,
                x.ExternalDeviceId,
                x.IsActive,
                x.IngestApiKeyHash != null && x.IngestApiKeyHash != "",
                x.IngestApiKeyCreatedUtc,
                x.CreatedUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<PortalDeviceCreatedResponse> CreateAsync(
        Guid customerId,
        CreatePortalDeviceRequest request,
        CancellationToken cancellationToken)
    {
        var deviceName = request.DeviceName.Trim();
        if (deviceName.Length is < 1 or > 200)
        {
            throw new ArgumentException("Device name is required (max 200 characters).", nameof(request));
        }

        var externalId = string.IsNullOrWhiteSpace(request.ExternalDeviceId)
            ? $"device-{Guid.NewGuid():N}"
            : request.ExternalDeviceId.Trim();

        if (externalId.Length is < 1 or > 100)
        {
            throw new ArgumentException("External device id is invalid.", nameof(request));
        }

        var exists = await dbContext.Devices.AnyAsync(
            x => x.ExternalDeviceId == externalId,
            cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException($"External device id '{externalId}' is already registered.");
        }

        var apiKey = GenerateApiKey();
        var now = DateTimeOffset.UtcNow;
        var device = new MonitoredDevice
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            DeviceName = deviceName,
            ExternalDeviceId = externalId,
            IngestApiKeyHash = passwordHasher.HashPassword(apiKey),
            IngestApiKeyCreatedUtc = now,
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        dbContext.Devices.Add(device);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new PortalDeviceCreatedResponse(device.Id, device.DeviceName, device.ExternalDeviceId, apiKey);
    }

    public async Task<PortalDeviceCreatedResponse?> RotateIngestKeyAsync(
        Guid customerId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var device = await dbContext.Devices
            .SingleOrDefaultAsync(x => x.Id == deviceId && x.CustomerId == customerId, cancellationToken);

        if (device is null)
        {
            return null;
        }

        var apiKey = GenerateApiKey();
        var now = DateTimeOffset.UtcNow;
        device.IngestApiKeyHash = passwordHasher.HashPassword(apiKey);
        device.IngestApiKeyCreatedUtc = now;
        device.UpdatedUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new PortalDeviceCreatedResponse(device.Id, device.DeviceName, device.ExternalDeviceId, apiKey);
    }

    private static string GenerateApiKey() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
}
