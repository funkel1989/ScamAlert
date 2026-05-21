using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using ScamAlert.Data;

namespace ScamAlert.Api.Services.Auth;

public interface IPortalCookieSignInService
{
    Task<bool> TrySignInAsync(HttpContext httpContext, string username, string password, CancellationToken cancellationToken);

    Task<bool> TrySignInByCustomerIdAsync(HttpContext httpContext, Guid customerId, CancellationToken cancellationToken);
}

public sealed class PortalCookieSignInService(
    IAuthCredentialService authCredentialService,
    ScamAlertDbContext dbContext) : IPortalCookieSignInService
{
    public async Task<bool> TrySignInAsync(
        HttpContext httpContext,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var result = await authCredentialService.ValidateAsync(username, password, cancellationToken);
        if (!result.Success)
        {
            return false;
        }

        await SignInPrincipalAsync(httpContext, result.Username, result.Roles, result.CustomerScope);
        return true;
    }

    public async Task<bool> TrySignInByCustomerIdAsync(
        HttpContext httpContext,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var scopeId = customerId.ToString("D");
        var user = await dbContext.AuthUserCredentials
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Where(x => x.CustomerScopeCsv == scopeId)
            .OrderBy(x => x.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return false;
        }

        var roles = user.RolesCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var customerScope = user.CustomerScopeCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await SignInPrincipalAsync(httpContext, user.Username, roles, customerScope);
        return true;
    }

    private static Task SignInPrincipalAsync(
        HttpContext httpContext,
        string username,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> customerScope)
    {
        var principal = PortalClaimsPrincipalFactory.Create(username, roles, customerScope);
        return httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    }
}
