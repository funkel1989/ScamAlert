using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ScamAlert.Api.Contracts;
using ScamAlert.Api.Services.Pairing;

namespace ScamAlert.Api.Controllers;

[ApiController]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[Route("api/setup")]
public sealed class SetupController(IDevicePairingService pairingService) : ControllerBase
{
    [HttpPost("redeem")]
    [EnableRateLimiting("setup-redeem")]
    public async Task<IActionResult> Redeem([FromBody] DevicePairingRedeemRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new { error = "Pairing code is required." });
        }

        var result = await pairingService.RedeemAsync(request.Code, cancellationToken);
        return result is null ? NotFound(new { error = "Invalid or expired pairing code." }) : Ok(result);
    }
}
