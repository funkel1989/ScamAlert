using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using ScamAlert.Api.Services.Auth;
using ScamAlert.Data;

namespace ScamAlert.Api.Services.Portal;

public sealed class CustomerPortalContext(
    ScamAlertDbContext dbContext,
    ICurrentUserAccessService userAccess) : ICustomerPortalContext
{
    public async Task<Guid?> TryGetSingleCustomerIdAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (userAccess.HasGlobalAccess(user))
        {
            return null;
        }

        var fromClaims = userAccess.GetAllowedCustomerIds(user);
        if (fromClaims.Count == 1)
        {
            return fromClaims.First();
        }

        if (fromClaims.Count > 1)
        {
            return null;
        }

        var email = user.Identity?.Name
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var customerId = await dbContext.Customers.AsNoTracking()
            .Where(x => x.Email == email.Trim())
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);

        return customerId;
    }
}
