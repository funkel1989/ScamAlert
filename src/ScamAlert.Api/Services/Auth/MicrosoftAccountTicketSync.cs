using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.EntityFrameworkCore;
using ScamAlert.Data;

namespace ScamAlert.Api.Services.Auth;

public static class MicrosoftAccountTicketSync
{
    public static void Attach(MicrosoftAccountOptions options)
    {
        options.Events = new OAuthEvents
        {
            OnTicketReceived = async ctx =>
            {
                await using var scope = ctx.HttpContext.RequestServices.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ScamAlertDbContext>();
                var email = ctx.Principal?.FindFirstValue(ClaimTypes.Email)
                    ?? ctx.Principal?.FindFirstValue("preferred_username");

                if (string.IsNullOrWhiteSpace(email))
                {
                    ctx.Fail("No email from Microsoft.");
                    return;
                }

                var normalized = email.Trim();
                var user = await db.AuthUserCredentials.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Username == normalized, ctx.HttpContext.RequestAborted);

                if (user is null || !user.IsActive)
                {
                    ctx.Fail("Create an account first, using the same email as your Microsoft account.");
                    return;
                }

                var roles = user.RolesCsv
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var customerScope = user.CustomerScopeCsv
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                ctx.Principal = PortalClaimsPrincipalFactory.Create(user.Username, roles, customerScope);
            }
        };
    }
}
