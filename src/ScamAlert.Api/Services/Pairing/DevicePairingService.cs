using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScamAlert.Api.Contracts;
using ScamAlert.Api.Services.Auth;
using ScamAlert.Api.Services.Web;
using ScamAlert.Data;
using ScamAlert.Data.Entities;
using ScamAlert.Data.Enums;

namespace ScamAlert.Api.Services.Pairing;

public sealed class DevicePairingService(
    ScamAlertDbContext dbContext,
    IPasswordHasher passwordHasher,
    IOptions<PairingOptions> pairingOptions,
    IOptions<WebSiteOptions> webOptions) : IDevicePairingService
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<DevicePairingCodeResponse?> CreateCodeAsync(
        Guid customerId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var device = await dbContext.Devices
            .SingleOrDefaultAsync(x => x.Id == deviceId && x.CustomerId == customerId && x.IsActive, cancellationToken);
        if (device is null)
        {
            return null;
        }

        var hasActiveSubscription = await dbContext.Subscriptions.AsNoTracking()
            .AnyAsync(
                x => x.CustomerId == customerId && x.Status == SubscriptionStatus.Active,
                cancellationToken);
        if (!hasActiveSubscription)
        {
            throw new InvalidOperationException("An active subscription is required before pairing a device.");
        }

        var now = DateTimeOffset.UtcNow;
        var expiryMinutes = Math.Clamp(pairingOptions.Value.CodeExpiryMinutes, 5, 60);
        var plainCode = GenerateCode();
        var entity = new DevicePairingCode
        {
            Id = Guid.NewGuid(),
            DeviceId = device.Id,
            CodeHash = passwordHasher.HashPassword(NormalizeCode(plainCode)),
            CreatedUtc = now,
            ExpiresUtc = now.AddMinutes(expiryMinutes)
        };

        dbContext.DevicePairingCodes.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new DevicePairingCodeResponse(plainCode, entity.ExpiresUtc);
    }

    public async Task<DevicePairingRedeemResponse?> RedeemAsync(string code, CancellationToken cancellationToken)
    {
        var normalized = NormalizeCode(code);
        if (normalized.Length is < 6 or > 12)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var candidates = await dbContext.DevicePairingCodes
            .Include(x => x.Device)
            .ThenInclude(x => x.Customer)
            .Where(x => x.RedeemedUtc == null && x.ExpiresUtc > now)
            .OrderByDescending(x => x.CreatedUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        DevicePairingCode? match = null;
        foreach (var candidate in candidates)
        {
            if (passwordHasher.Verify(candidate.CodeHash, normalized))
            {
                match = candidate;
                break;
            }
        }

        if (match is null || !match.Device.IsActive)
        {
            return null;
        }

        var hasActiveSubscription = await dbContext.Subscriptions.AsNoTracking()
            .AnyAsync(
                x => x.CustomerId == match.Device.CustomerId && x.Status == SubscriptionStatus.Active,
                cancellationToken);
        if (!hasActiveSubscription)
        {
            return null;
        }

        var apiKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        match.Device.IngestApiKeyHash = passwordHasher.HashPassword(apiKey);
        match.Device.IngestApiKeyCreatedUtc = now;
        match.Device.UpdatedUtc = now;
        match.RedeemedUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        var baseUrl = webOptions.Value.PublicBaseUrl.TrimEnd('/');
        return new DevicePairingRedeemResponse(
            baseUrl,
            match.Device.ExternalDeviceId,
            apiKey,
            match.Device.DeviceName);
    }

    private static string GenerateCode()
    {
        Span<char> buffer = stackalloc char[8];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        }

        return new string(buffer);
    }

    private static string NormalizeCode(string code) =>
        code.Trim().ToUpperInvariant();
}
