using System.Security.Claims;

namespace ScamAlert.Api.Services.Auth;

public interface ICurrentUserAccessService
{
    bool HasGlobalAccess(ClaimsPrincipal user);
    bool CanAccessCustomer(ClaimsPrincipal user, Guid customerId);
    IReadOnlyCollection<Guid> GetAllowedCustomerIds(ClaimsPrincipal user);
}

public sealed class CurrentUserAccessService : ICurrentUserAccessService
{
    public bool HasGlobalAccess(ClaimsPrincipal user)
    {
        return user.IsInRole("admin") ||
               user.Claims.Any(x => x.Type == AuthClaimTypes.CustomerAll && x.Value == "true");
    }

    public bool CanAccessCustomer(ClaimsPrincipal user, Guid customerId)
    {
        if (HasGlobalAccess(user))
        {
            return true;
        }

        return GetAllowedCustomerIds(user).Contains(customerId);
    }

    public IReadOnlyCollection<Guid> GetAllowedCustomerIds(ClaimsPrincipal user)
    {
        var ids = user.Claims
            .Where(x => x.Type == AuthClaimTypes.CustomerId)
            .Select(x => Guid.TryParse(x.Value, out var parsed) ? parsed : (Guid?)null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();

        return ids;
    }
}
