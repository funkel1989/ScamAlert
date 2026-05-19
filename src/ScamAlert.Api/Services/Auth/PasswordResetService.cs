using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScamAlert.Api.Services.Email;
using ScamAlert.Api.Services.Web;
using ScamAlert.Data;
using ScamAlert.Data.Entities;

namespace ScamAlert.Api.Services.Auth;

public interface IPasswordResetService
{
    Task RequestResetAsync(string email, CancellationToken cancellationToken);
    Task<bool> ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken);
}

public sealed class PasswordResetService(
    ScamAlertDbContext dbContext,
    IPasswordHasher passwordHasher,
    IEmailSender emailSender,
    IOptions<WebSiteOptions> webOptions,
    ILogger<PasswordResetService> logger) : IPasswordResetService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    public async Task RequestResetAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = email.Trim();
        if (normalized.Length is < 3 or > 320)
        {
            return;
        }

        var user = await dbContext.AuthUserCredentials
            .SingleOrDefaultAsync(x => x.Username == normalized && x.IsActive, cancellationToken);

        if (user is null)
        {
            return;
        }

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var tokenHash = HashToken(rawToken);
        var now = DateTimeOffset.UtcNow;

        var activeTokens = await dbContext.PasswordResetTokens
            .Where(x => x.Username == normalized && !x.IsUsed && x.ExpiresUtc > now)
            .ToListAsync(cancellationToken);

        foreach (var existing in activeTokens)
        {
            existing.IsUsed = true;
        }

        dbContext.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            Username = normalized,
            TokenHash = tokenHash,
            ExpiresUtc = now.Add(TokenLifetime),
            IsUsed = false,
            CreatedUtc = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        var resetUrl = $"{TrimSlash(webOptions.Value.PublicBaseUrl)}/reset-password?token={Uri.EscapeDataString(rawToken)}";
        try
        {
            await emailSender.SendAsync(
                normalized,
                "Reset your ScamAlert password",
                $"Use this link within one hour to reset your password:\n\n{resetUrl}\n\nIf you did not request this, ignore this email.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send password reset email to {Email}", normalized);
        }
    }

    public async Task<bool> ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || newPassword.Length < 8)
        {
            return false;
        }

        var tokenHash = HashToken(token.Trim());
        var now = DateTimeOffset.UtcNow;

        var record = await dbContext.PasswordResetTokens
            .SingleOrDefaultAsync(
                x => x.TokenHash == tokenHash && !x.IsUsed && x.ExpiresUtc > now,
                cancellationToken);

        if (record is null)
        {
            return false;
        }

        var user = await dbContext.AuthUserCredentials
            .SingleOrDefaultAsync(x => x.Username == record.Username && x.IsActive, cancellationToken);

        if (user is null)
        {
            return false;
        }

        user.PasswordHash = passwordHasher.HashPassword(newPassword);
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.UpdatedUtc = now;
        record.IsUsed = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private static string TrimSlash(string url) => url.TrimEnd('/');
}
