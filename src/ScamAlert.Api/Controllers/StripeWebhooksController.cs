using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ScamAlert.Api.Services.Stripe;
using Stripe;

namespace ScamAlert.Api.Controllers;

[ApiController]
[Route("api/webhooks/stripe")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public sealed class StripeWebhooksController(
    IOptions<StripeOptions> stripeOptions,
    IStripeSubscriptionWebhookProcessor webhookProcessor,
    ILogger<StripeWebhooksController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post(CancellationToken cancellationToken)
    {
        var opts = stripeOptions.Value;
        if (string.IsNullOrWhiteSpace(opts.WebhookSecret))
        {
            return NotFound();
        }

        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(Request.Headers["Stripe-Signature"]))
        {
            return BadRequest();
        }

        try
        {
            var stripeEvent = EventUtility.ConstructEvent(
                json,
                Request.Headers["Stripe-Signature"],
                opts.WebhookSecret,
                throwOnApiVersionMismatch: false);

            await webhookProcessor.ProcessAsync(stripeEvent, cancellationToken);
            return Ok();
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Invalid Stripe webhook.");
            return BadRequest();
        }
    }
}
