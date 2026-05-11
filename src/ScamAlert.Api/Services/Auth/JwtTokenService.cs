using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ScamAlert.Api.Services.Auth;

public sealed class JwtTokenService(IOptions<AuthOptions> authOptions) : ITokenService
{
    public string CreateAccessToken(
        string username,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> customerScope)
    {
        var jwt = authOptions.Value.Jwt;
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, username),
            new(JwtRegisteredClaimNames.UniqueName, username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
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

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: jwt.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(Math.Max(1, jwt.AccessTokenMinutes)),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
