using Microsoft.AspNetCore.Mvc;
using ScamAlert.Api.Contracts;
using ScamAlert.Api.Services.Portal;

namespace ScamAlert.Api.Controllers;

[Route("api/portal/devices")]
public sealed class PortalDevicesController(
    ICustomerPortalContext portalContext,
    IPortalDeviceService deviceService) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var customerId = await portalContext.TryGetSingleCustomerIdAsync(User, cancellationToken);
        if (customerId is null)
        {
            return BadRequest(new { error = "Portal devices require a login linked to exactly one organization." });
        }

        var devices = await deviceService.ListAsync(customerId.Value, cancellationToken);
        return Ok(devices);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreatePortalDeviceRequest request, CancellationToken cancellationToken)
    {
        var customerId = await portalContext.TryGetSingleCustomerIdAsync(User, cancellationToken);
        if (customerId is null)
        {
            return BadRequest(new { error = "Portal devices require a login linked to exactly one organization." });
        }

        try
        {
            var created = await deviceService.CreateAsync(customerId.Value, request, cancellationToken);
            return Created(string.Empty, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/rotate-key")]
    public async Task<IActionResult> RotateKey(Guid id, CancellationToken cancellationToken)
    {
        var customerId = await portalContext.TryGetSingleCustomerIdAsync(User, cancellationToken);
        if (customerId is null)
        {
            return BadRequest(new { error = "Portal devices require a login linked to exactly one organization." });
        }

        var rotated = await deviceService.RotateIngestKeyAsync(customerId.Value, id, cancellationToken);
        return rotated is null ? NotFound() : Ok(rotated);
    }
}
