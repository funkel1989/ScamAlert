using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScamAlert.Api.Contracts;
using ScamAlert.Api.Services.Alerts;
using ScamAlert.Data;

namespace ScamAlert.Api.Controllers;

public sealed class AlertsController(
    ScamAlertDbContext dbContext,
    AlertWorkflowService alertWorkflowService) : BaseApiController
{
    [HttpPost]
    public async Task<IActionResult> Raise(RaiseAlertRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var alert = await alertWorkflowService.RaiseAlertAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = alert.Id }, new
            {
                alert.Id,
                alert.CustomerId,
                alert.DeviceId,
                alert.ResolutionStatus,
                alert.AcknowledgedByContactId,
                alert.CreatedUtc
            });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var alert = await dbContext.AlertEvents
            .AsNoTracking()
            .Include(x => x.NotificationAttempts)
            .ThenInclude(x => x.Contact)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (alert is null)
        {
            return NotFound();
        }

        var response = new AlertDetailsResponse(
            alert.Id,
            alert.CustomerId,
            alert.DeviceId,
            alert.SourceIp,
            alert.DestinationPort,
            alert.Service,
            alert.ResolutionStatus,
            alert.AcknowledgedByContactId,
            alert.CreatedUtc,
            alert.NotificationAttempts
                .OrderBy(x => x.AttemptedUtc)
                .Select(x => new NotificationAttemptResponse(
                    x.ContactId,
                    x.Contact.FullName,
                    x.Contact.EscalationOrder,
                    x.Channel,
                    x.Outcome,
                    x.ProviderMessageId,
                    x.AttemptedUtc))
                .ToList());

        return Ok(response);
    }
}
