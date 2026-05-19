using Microsoft.AspNetCore.Mvc;
using ScamAlert.Api.Contracts;
using ScamAlert.Api.Services.Portal;

namespace ScamAlert.Api.Controllers;

[Route("api/portal/contacts")]
public sealed class PortalContactsController(
    ICustomerPortalContext portalContext,
    IPortalContactService contactService) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var customerId = await portalContext.TryGetSingleCustomerIdAsync(User, cancellationToken);
        if (customerId is null)
        {
            return BadRequest(new { error = "Portal contacts require a login linked to exactly one organization." });
        }

        var contacts = await contactService.ListAsync(customerId.Value, cancellationToken);
        return Ok(contacts);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreatePortalContactRequest request, CancellationToken cancellationToken)
    {
        var customerId = await portalContext.TryGetSingleCustomerIdAsync(User, cancellationToken);
        if (customerId is null)
        {
            return BadRequest(new { error = "Portal contacts require a login linked to exactly one organization." });
        }

        try
        {
            var created = await contactService.CreateAsync(customerId.Value, request, cancellationToken);
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

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdatePortalContactRequest request, CancellationToken cancellationToken)
    {
        var customerId = await portalContext.TryGetSingleCustomerIdAsync(User, cancellationToken);
        if (customerId is null)
        {
            return BadRequest(new { error = "Portal contacts require a login linked to exactly one organization." });
        }

        try
        {
            var updated = await contactService.UpdateAsync(customerId.Value, id, request, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
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

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var customerId = await portalContext.TryGetSingleCustomerIdAsync(User, cancellationToken);
        if (customerId is null)
        {
            return BadRequest(new { error = "Portal contacts require a login linked to exactly one organization." });
        }

        try
        {
            var deleted = await contactService.DeleteAsync(customerId.Value, id, cancellationToken);
            return deleted ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
