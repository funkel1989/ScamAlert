using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScamAlert.Api.Services.Auth;
using ScamAlert.Api.Services.Signup;

namespace ScamAlert.Api.Controllers;

[AllowAnonymous]
public sealed class SignupCompleteController(
    ISignupCheckoutCompletionService checkoutCompletion,
    ISignupSignInTicketStore signInTicketStore,
    IPortalCookieSignInService portalSignIn) : Controller
{
    [HttpGet("/signup/complete")]
    public async Task<IActionResult> Complete(
        [FromQuery] string? session_id,
        [FromQuery] string? ticket,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(ticket) && signInTicketStore.TryConsume(ticket, out var ticketCustomerId))
        {
            if (await portalSignIn.TrySignInByCustomerIdAsync(HttpContext, ticketCustomerId, cancellationToken))
            {
                return Redirect("/dashboard?welcome=true");
            }

            return RedirectToLogin();
        }

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
