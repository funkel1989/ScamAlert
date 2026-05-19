using System.Security.Claims;

namespace ScamAlert.Api.Services.Portal;

public interface ICustomerPortalContext
{
    /// <summary>
    /// Returns the customer id for a portal user scoped to exactly one organization.
    /// </summary>
    Task<Guid?> TryGetSingleCustomerIdAsync(ClaimsPrincipal user, CancellationToken cancellationToken);
}
