using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ScamAlert.Api.Contracts;
using ScamAlert.Api.Services.Audit;
using ScamAlert.Api.Services.Auth;

namespace ScamAlert.Api.Controllers;

[AllowAnonymous]
public sealed class AuthController(
    IAuthCredentialService authCredentialService,
    IOptions<AuthOptions> authOptions,
    ITokenService tokenService,
    IAuditLogger auditLogger) : BaseApiController
{
    [HttpPost("token")]
    [EnableRateLimiting("auth-token")]
    public async Task<IActionResult> Token(TokenRequest request, CancellationToken cancellationToken)
    {
        var result = await authCredentialService.ValidateAsync(request.Username, request.Password, cancellationToken);
        if (!result.Success)
        {
            auditLogger.AuthFailed(request.Username, result.IsLockedOut, HttpContext.Connection.RemoteIpAddress?.ToString());
            if (result.IsLockedOut)
            {
                return Unauthorized(new { error = "Account temporarily locked due to failed login attempts.", lockedUntilUtc = result.LockoutEndUtc });
            }

            if (result.IsEmailUnverified)
            {
                return StatusCode(403, new { error = "Email address not verified. Check your inbox for a verification link.", code = "email_unverified" });
            }

            return Unauthorized(new { error = "Invalid username or password." });
        }

        var accessToken = tokenService.CreateAccessToken(result.Username, result.Roles, result.CustomerScope);
        var expiresUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, authOptions.Value.Jwt.AccessTokenMinutes));
        auditLogger.AuthTokenIssued(result.Username, HttpContext.Connection.RemoteIpAddress?.ToString());
        return Ok(new TokenResponse(accessToken, "Bearer", expiresUtc));
    }
}
