using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ScamAlert.Api.Services.Auth;
using ScamAlert.Api.Services.Signup;

namespace ScamAlert.Api.Controllers;

[AllowAnonymous]
public sealed class SignupCompleteController(
    ISignupCheckoutCompletionService checkoutCompletion,
    ISignupSignInTicketStore signInTicketStore,
    IPortalCookieSignInService portalSignIn) : Controller
{
    [HttpPost("/signup/complete")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("signup-complete")]
    public async Task<IActionResult> CompleteWithTicket(
        [FromForm] string ticket,
        CancellationToken cancellationToken)
    {
        if (!signInTicketStore.TryConsume(ticket, out var customerId))
        {
            return RedirectToLogin();
        }

        if (!await portalSignIn.TrySignInByCustomerIdAsync(HttpContext, customerId, cancellationToken))
        {
            return RedirectToLogin();
        }

        return Redirect("/dashboard?welcome=true");
    }

    [HttpGet("/signup/complete")]
    [EnableRateLimiting("signup-complete")]
    public async Task<IActionResult> CompleteStripe(
        [FromQuery] string? session_id,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session_id))
        {
            return RedirectToLogin();
        }

        var completion = await checkoutCompletion.TryCompleteStripeSessionAsync(session_id, cancellationToken);
        if (!completion.Success || completion.CustomerId is not { } customerId)
        {
            return Redirect("/login?returnUrl=/dashboard&error=checkout");
        }

        if (!await portalSignIn.TrySignInByCustomerIdAsync(HttpContext, customerId, cancellationToken))
        {
            return RedirectToLogin();
        }

        return Redirect("/dashboard?welcome=true");
    }

    private RedirectResult RedirectToLogin() =>
        Redirect("/login?returnUrl=/dashboard&welcome=true");
}
