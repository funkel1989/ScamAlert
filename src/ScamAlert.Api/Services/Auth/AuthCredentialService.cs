using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScamAlert.Data;
using ScamAlert.Data.Entities;

namespace ScamAlert.Api.Services.Auth;

public interface IAuthCredentialService
{
    Task<AuthValidationResult> ValidateAsync(string username, string password, CancellationToken cancellationToken);
}

public sealed record AuthValidationResult(
    bool Success,
    bool IsLockedOut,
    bool IsEmailUnverified,
    DateTimeOffset? LockoutEndUtc,
    string Username,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> CustomerScope);

public sealed class AuthCredentialService(
    ScamAlertDbContext dbContext,
    IPasswordHasher passwordHasher,
    IOptions<AuthOptions> authOptions) : IAuthCredentialService
{
    public async Task<AuthValidationResult> ValidateAsync(string username, string password, CancellationToken cancellationToken)
    {
        var normalized = username.Trim();
        var user = await dbContext.AuthUserCredentials
            .SingleOrDefaultAsync(x => x.Username == normalized, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return new AuthValidationResult(false, false, false, null, normalized, [], []);
        }

        var now = DateTimeOffset.UtcNow;
        if (user.LockoutEndUtc is { } lockoutEnd && lockoutEnd > now)
        {
            return new AuthValidationResult(false, true, false, lockoutEnd, user.Username, [], []);
        }

        if (!passwordHasher.Verify(user.PasswordHash, password))
        {
            user.FailedLoginCount += 1;
            user.UpdatedUtc = now;
            var lockoutOptions = authOptions.Value.Lockout;
            if (user.FailedLoginCount >= Math.Max(1, lockoutOptions.MaxFailedAttempts))
            {
                user.LockoutEndUtc = now.AddMinutes(Math.Max(1, lockoutOptions.LockoutMinutes));
                user.FailedLoginCount = 0;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return new AuthValidationResult(false, user.LockoutEndUtc is { } future && future > now, false, user.LockoutEndUtc, user.Username, [], []);
        }

        if (!user.IsEmailVerified)
        {
            return new AuthValidationResult(false, false, true, null, user.Username, [], []);
        }

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginUtc = now;
        user.UpdatedUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        var roles = user.RolesCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var customerScope = user.CustomerScopeCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new AuthValidationResult(true, false, false, null, user.Username, roles, customerScope);
    }
}
