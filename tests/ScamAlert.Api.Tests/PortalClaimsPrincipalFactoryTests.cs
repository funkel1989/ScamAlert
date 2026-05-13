using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using ScamAlert.Api.Services.Auth;
namespace ScamAlert.Api.Tests;

public sealed class PortalClaimsPrincipalFactoryTests
{
    [Fact]
    public void Create_maps_customer_scope_claims()
    {
        var principal = PortalClaimsPrincipalFactory.Create(
            "user@example.com",
            ["operator"],
            [Guid.NewGuid().ToString("D")]);

        Assert.Equal("user@example.com", principal.Identity!.Name);
        Assert.True(principal.IsInRole("operator"));
        Assert.Single(principal.FindAll(AuthClaimTypes.CustomerId));
        Assert.Null(principal.FindFirst(AuthClaimTypes.CustomerAll));
    }

    [Fact]
    public void Create_star_scope_sets_customer_all()
    {
        var principal = PortalClaimsPrincipalFactory.Create("admin", ["admin"], ["*"]);
        Assert.NotNull(principal.FindFirst(AuthClaimTypes.CustomerAll));
    }

    [Fact]
    public void Create_uses_cookie_scheme()
    {
        var principal = PortalClaimsPrincipalFactory.Create("a", [], []);
        var id = principal.Identity as ClaimsIdentity;
        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, id!.AuthenticationType);
    }
}
