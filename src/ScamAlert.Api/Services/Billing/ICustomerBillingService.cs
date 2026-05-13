using System.Security.Claims;
using ScamAlert.Api.Contracts;

namespace ScamAlert.Api.Services.Billing;

public interface ICustomerBillingService
{
    /// <summary>Returns null when the user is not a single-tenant customer account (e.g. global admin).</summary>
    Task<BillingSummaryResponse?> GetSummaryAsync(ClaimsPrincipal user, CancellationToken cancellationToken);

    Task<string> CreateCustomerPortalUrlAsync(ClaimsPrincipal user, CancellationToken cancellationToken);

    Task ChangePlanAsync(ClaimsPrincipal user, string planCode, CancellationToken cancellationToken);
}
