using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ScamAlert.Api.Services.Auth;
using ScamAlert.Data;

namespace ScamAlert.Api.Controllers;

public sealed class DevicesController(
    ScamAlertDbContext dbContext,
    IAuthorizationService authorizationService) : BaseApiController
{
    [HttpGet("{id:guid}/recent-connections")]
    public async Task<IActionResult> RecentConnections(
        Guid id,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);

        var device = await dbContext.Devices
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (device is null)
        {
            return NotFound();
        }

        var auth = await authorizationService.AuthorizeAsync(User, device.CustomerId, AuthPolicies.CustomerScope);
        if (!auth.Succeeded)
        {
            return Forbid();
        }

        var recent = await dbContext.AlertEvents
            .AsNoTracking()
            .Where(x => x.DeviceId == id)
            .OrderByDescending(x => x.CreatedUtc)
            .Take(take)
            .Select(x => new
            {
                x.Id,
                x.SourceIp,
                x.DestinationIp,
                x.DestinationPort,
                x.Service,
                x.Transport,
                x.Direction,
                x.ObservedBy,
                x.RuleApplied,
                x.DecisionReason,
                x.ResolutionStatus,
                x.CreatedUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            device.Id,
            device.DeviceName,
            device.ExternalDeviceId,
            recent
        });
    }
}
