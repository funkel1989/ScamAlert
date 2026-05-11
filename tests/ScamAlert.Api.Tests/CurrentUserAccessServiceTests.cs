using System.Security.Claims;
using ScamAlert.Api.Services.Auth;

namespace ScamAlert.Api.Tests;

public sealed class CurrentUserAccessServiceTests
{
    private readonly CurrentUserAccessService service = new();

    [Fact]
    public void CanAccessCustomer_returns_true_when_customer_claim_present()
    {
        var customerId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(AuthClaimTypes.CustomerId, customerId.ToString())],
            "test"));

        var allowed = service.CanAccessCustomer(principal, customerId);

        Assert.True(allowed);
    }

    [Fact]
    public void CanAccessCustomer_returns_true_when_admin_role_present()
    {
        var customerId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "admin")],
            "test"));

        var allowed = service.CanAccessCustomer(principal, customerId);

        Assert.True(allowed);
    }
}
