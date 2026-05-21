using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ScamAlert.Api.Contracts;
using ScamAlert.Api.Services.Billing;

namespace ScamAlert.Api.Controllers;

public sealed class BillingController(ICustomerBillingService billing) : BaseApiController
{
    [HttpGet("summary")]
    [EnableRateLimiting("billing-summary")]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
    {
        var summary = await billing.GetSummaryAsync(User, cancellationToken);
        if (summary is null)
        {
            return Forbid();
        }

        return Ok(summary);
    }

    [HttpPost("portal")]
    [EnableRateLimiting("billing-mutate")]
    public async Task<IActionResult> CreatePortalSession(CancellationToken cancellationToken)
    {
        try
        {
            var url = await billing.CreateCustomerPortalUrlAsync(User, cancellationToken);
            return Ok(new CustomerPortalUrlResponse(url));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("address")]
    [EnableRateLimiting("billing-mutate")]
    public async Task<IActionResult> UpdateBillingAddress(
        [FromBody] UpdateBillingAddressRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Address is required." });
        }

        try
        {
            await billing.UpdateBillingAddressAsync(User, request, cancellationToken);
            return Ok(new { message = "Billing address saved." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("change-plan")]
    [EnableRateLimiting("billing-mutate")]
    public async Task<IActionResult> ChangePlan([FromBody] ChangePlanRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlanCode))
        {
            return BadRequest(new { error = "PlanCode is required." });
        }

        try
        {
            await billing.ChangePlanAsync(User, request.PlanCode, cancellationToken);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
