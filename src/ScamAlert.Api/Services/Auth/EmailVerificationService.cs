using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScamAlert.Api.Services.Email;
using ScamAlert.Api.Services.Web;
using ScamAlert.Data;
using ScamAlert.Data.Entities;

namespace ScamAlert.Api.Services.Auth;

public interface IEmailVerificationService
{
    Task SendVerificationEmailAsync(string email, CancellationToken cancellationToken);
    Task<bool> VerifyAsync(string token, CancellationToken cancellationToken);
}

public sealed class EmailVerificationService(
    ScamAlertDbContext dbContext,
    IEmailSender emailSender,
    IOptions<WebSiteOptions> webOptions,
    ILogger<EmailVerificationService> logger) : IEmailVerificationService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);

    public async Task SendVerificationEmailAsync(string email, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var activeTokens = await dbContext.EmailVerificationTokens
            .Where(x => x.Username == email && !x.IsUsed && x.ExpiresUtc > now)
            .ToListAsync(cancellationToken);

        foreach (var existing in activeTokens)
        {
            existing.IsUsed = true;
        }

        var rawToken = GenerateToken();
        dbContext.EmailVerificationTokens.Add(new EmailVerificationToken
        {
            Id = Guid.NewGuid(),
            Username = email,
            TokenHash = HashToken(rawToken),
            ExpiresUtc = now.Add(TokenLifetime),
            IsUsed = false,
            CreatedUtc = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        var verifyUrl = $"{TrimSlash(webOptions.Value.PublicBaseUrl)}/verify-email?token={Uri.EscapeDataString(rawToken)}";
        try
        {
            await emailSender.SendAsync(
                email,
                "Verify your ScamAlert email address",
                $"Please verify your email address by clicking the link below. This link expires in 24 hours.\n\n{verifyUrl}\n\nIf you did not create a ScamAlert account, you can ignore this email.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send verification email to {Email}", email);
        }
    }

    public async Task<bool> VerifyAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var tokenHash = HashToken(token.Trim());
        var now = DateTimeOffset.UtcNow;

        var record = await dbContext.EmailVerificationTokens
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

        user.IsEmailVerified = true;
        user.UpdatedUtc = now;
        record.IsUsed = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string GenerateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private static string TrimSlash(string url) => url.TrimEnd('/');
}
