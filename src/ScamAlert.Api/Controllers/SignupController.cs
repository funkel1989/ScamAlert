using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ScamAlert.Api.Contracts;
using ScamAlert.Api.Services.Billing;
using ScamAlert.Api.Services.Signup;

namespace ScamAlert.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public sealed class SignupController(ISignupService signupService) : ControllerBase
{
    [HttpGet("plans")]
    public IActionResult Plans([FromServices] IOptions<BillingOptions> billing)
    {
        var plans = billing.Value.Tiers
            .Where(t => !string.IsNullOrWhiteSpace(t.PlanCode))
            .Select(t => new SignupPlanResponse(t.PlanCode, string.IsNullOrWhiteSpace(t.DisplayName) ? t.PlanCode : t.DisplayName))
            .ToList();
        return Ok(plans);
    }

    [HttpPost]
    [EnableRateLimiting("signup")]
    public async Task<IActionResult> Register(SelfServeSignupRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var withIp = request with { ConsentIpAddress = ip };
            var result = await signupService.RegisterAndStartCheckoutAsync(withIp, cancellationToken);
            return Ok(new
            {
                result.CustomerId,
                checkoutUrl = result.CheckoutUrl,
                signInTicket = result.SignInTicket,
                provisionedDevices = result.ProvisionedDevices
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new { error = "An account with that email address already exists." });
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { error = "Signup could not be completed. Check your request and try again." });
        }
    }
}
