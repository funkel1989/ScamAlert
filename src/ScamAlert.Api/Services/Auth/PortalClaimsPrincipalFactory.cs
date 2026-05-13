using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ScamAlert.Api.Services.Auth;

public static class PortalClaimsPrincipalFactory
{
    public static ClaimsPrincipal Create(string username, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> customerScope)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, username),
            new(JwtRegisteredClaimNames.UniqueName, username),
            new(ClaimTypes.Name, username),
            new(ClaimTypes.NameIdentifier, username)
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        foreach (var scope in customerScope)
        {
            if (scope == "*")
            {
                claims.Add(new Claim(AuthClaimTypes.CustomerAll, "true"));
                continue;
            }

            claims.Add(new Claim(AuthClaimTypes.CustomerId, scope));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }
}
