using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ScamAlert.Api.Services.Auth;
using ScamAlert.Api.Services.Validation;

namespace ScamAlert.Api.Controllers;

[AllowAnonymous]
public sealed class LoginController(
    IAuthCredentialService authCredentialService,
    IPortalCookieSignInService portalSignIn) : Controller
{
    [HttpPost("/login/sign-in")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth-token")]
    public async Task<IActionResult> SignIn(
        [FromForm] string email,
        [FromForm] string password,
        [FromForm] string? returnUrl,
        CancellationToken cancellationToken)
    {
        if (!EmailAddressValidator.TryValidate(email, out var normalizedEmail, out _))
        {
            return RedirectToLogin("invalid", returnUrl, normalizedEmail);
        }

        var result = await authCredentialService.ValidateAsync(normalizedEmail, password, cancellationToken);
        if (!result.Success)
        {
            return RedirectToLogin(result.IsLockedOut ? "locked" : "invalid", returnUrl, normalizedEmail);
        }

        await portalSignIn.SignInFromValidationAsync(HttpContext, result);
        return Redirect(SanitizeReturnUrl(returnUrl));
    }

    private RedirectResult RedirectToLogin(string error, string? returnUrl, string? email = null)
    {
        var query = new List<string> { $"error={Uri.EscapeDataString(error)}" };
        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            query.Add($"returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            query.Add($"email={Uri.EscapeDataString(email)}");
        }

        return Redirect($"/login?{string.Join('&', query)}");
    }

    private static string SanitizeReturnUrl(string? returnUrl) =>
        string.IsNullOrWhiteSpace(returnUrl) || !Uri.TryCreate(returnUrl, UriKind.Relative, out _)
            ? "/dashboard"
            : returnUrl;
}
