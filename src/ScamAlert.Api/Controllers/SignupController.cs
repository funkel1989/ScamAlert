using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ScamAlert.Api.Contracts;
using ScamAlert.Api.Services.Signup;

namespace ScamAlert.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public sealed class SignupController(ISignupService signupService) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("signup")]
    public async Task<IActionResult> Register(SelfServeSignupRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await signupService.RegisterAndStartCheckoutAsync(request, cancellationToken);
            return Ok(new { result.CustomerId, checkoutUrl = result.CheckoutUrl });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
