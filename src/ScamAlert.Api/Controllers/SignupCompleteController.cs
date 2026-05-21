using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScamAlert.Api.Services.Auth;
using ScamAlert.Api.Services.Signup;

namespace ScamAlert.Api.Controllers;

[AllowAnonymous]
public sealed class SignupCompleteController(
    ISignupCheckoutCompletionService checkoutCompletion,
    IPortalCookieSignInService portalSignIn) : Controller
{
    [HttpGet("/signup/complete")]
    public async Task<IActionResult> Complete([FromQuery] string? session_id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session_id))
        {
            return RedirectToLogin();
        }

        var completion = await checkoutCompletion.TryCompleteStripeSessionAsync(session_id, cancellationToken);
        if (!completion.Success || completion.CustomerId is not { } customerId)
        {
            return Redirect("/login?returnUrl=/dashboard&checkout=failed");
        }

        if (!await portalSignIn.TrySignInByCustomerIdAsync(HttpContext, customerId, cancellationToken))
        {
            return RedirectToLogin();
        }

        return Redirect("/dashboard?welcome=1");
    }

    private RedirectResult RedirectToLogin() =>
        Redirect("/login?returnUrl=/dashboard&welcome=1");
}
