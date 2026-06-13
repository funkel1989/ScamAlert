using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ScamAlert.Api.Contracts;
using ScamAlert.Api.Services.Audit;
using ScamAlert.Api.Services.Alerts;
using ScamAlert.Api.Services.Auth;
using ScamAlert.Data;
using ScamAlert.Data.Enums;

namespace ScamAlert.Api.Controllers;

public sealed class AlertsController(
    ScamAlertDbContext dbContext,
    AlertWorkflowService alertWorkflowService,
    ICurrentUserAccessService userAccessService,
    IDeviceIngestAuthService deviceIngestAuthService,
    IAuthorizationService authorizationService,
    IAuditLogger auditLogger) : BaseApiController
{
    private const string DeviceIngestHeader = "X-ScamAlert-DeviceKey";

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? deviceId,
        [FromQuery] string? externalDeviceId,
        [FromQuery] AlertResolutionStatus? status,
        [FromQuery] string? sourceIp,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        IQueryable<ScamAlert.Data.Entities.AlertEvent> query = dbContext.AlertEvents
            .AsNoTracking()
            .Include(x => x.Device);

        if (!userAccessService.HasGlobalAccess(User))
        {
            var allowed = userAccessService.GetAllowedCustomerIds(User);
            query = query.Where(x => allowed.Contains(x.CustomerId));
        }

        if (deviceId.HasValue)
        {
            query = query.Where(x => x.DeviceId == deviceId.Value);
        }

        if (!string.IsNullOrWhiteSpace(externalDeviceId))
        {
            query = query.Where(x => x.Device.ExternalDeviceId == externalDeviceId);
        }

        if (status.HasValue)
        {
            query = query.Where(x => x.ResolutionStatus == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(sourceIp))
        {
            query = query.Where(x => x.SourceIp == sourceIp);
        }

        if (fromUtc.HasValue)
        {
            query = query.Where(x => x.CreatedUtc >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(x => x.CreatedUtc <= toUtc.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var results = await query
            .OrderByDescending(x => x.CreatedUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(alert => new AlertSummaryResponse(
                alert.Id,
                alert.CustomerId,
                alert.DeviceId,
                alert.SourceIp,
                alert.DestinationPort,
                alert.Service,
                alert.DestinationIp,
                alert.Transport,
                alert.Direction,
                alert.ObservedBy,
                alert.RuleApplied,
                alert.DecisionReason,
                alert.ResolutionStatus,
                alert.AcknowledgedByContactId,
                alert.CreatedUtc,
                alert.UpdatedUtc))
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            page,
            pageSize,
            total,
            items = results
        });
    }

    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    [HttpPost]
    [EnableRateLimiting("alert-ingest")]
    public async Task<IActionResult> Raise(RaiseAlertRequest request, CancellationToken cancellationToken)
    {
        var targetCustomerId = await ResolveCustomerIdByExternalDeviceIdAsync(request.ExternalDeviceId, cancellationToken);
        if (targetCustomerId is null)
        {
            return BadRequest(new { error = $"No active device found for external id '{request.ExternalDeviceId}'." });
        }

        if (!await IsAuthorizedToRaiseAsync(request.ExternalDeviceId, targetCustomerId.Value, cancellationToken))
        {
            return Unauthorized(new { error = "Unauthorized alert ingest request." });
        }

        try
        {
            var alert = await alertWorkflowService.RaiseAlertAsync(request, cancellationToken);
            auditLogger.AlertRaised(
                alert.Id,
                alert.CustomerId,
                alert.DeviceId,
                User.Identity?.IsAuthenticated == true ? "bearer" : "device-key",
                HttpContext.Connection.RemoteIpAddress?.ToString());
            return CreatedAtAction(nameof(GetById), new { id = alert.Id }, new
            {
                alert.Id,
                alert.CustomerId,
                alert.DeviceId,
                alert.ResolutionStatus,
                alert.AcknowledgedByContactId,
                alert.CreatedUtc,
                alert.UpdatedUtc
            });
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { error = "Alert could not be processed. Verify the device is active and the request is valid." });
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

        if (!await IsAuthorizedForCustomerAsync(alert.CustomerId))
        {
            return Forbid();
        }

        var response = new AlertDetailsResponse(
            alert.Id,
            alert.CustomerId,
            alert.DeviceId,
            alert.SourceIp,
            alert.DestinationPort,
            alert.Service,
            alert.DestinationIp,
            alert.Transport,
            alert.Direction,
            alert.ObservedBy,
            alert.RuleApplied,
            alert.DecisionReason,
            alert.Notes,
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

    [HttpPost("{id:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid id, AlertActionRequest request, CancellationToken cancellationToken)
    {
        var alert = await dbContext.AlertEvents.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (alert is null)
        {
            return NotFound();
        }

        if (!await IsAuthorizedForCustomerAsync(alert.CustomerId))
        {
            return Forbid();
        }

        alert.ResolutionStatus = AlertResolutionStatus.Acknowledged;
        alert.AcknowledgedByContactId = request.ContactId ?? alert.AcknowledgedByContactId;
        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            alert.Notes = request.Notes;
        }

        alert.UpdatedUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        auditLogger.AlertAction("acknowledge", alert.Id, alert.CustomerId, User.Identity?.Name ?? "unknown");
        return Ok(new { alert.Id, alert.ResolutionStatus, alert.AcknowledgedByContactId, alert.UpdatedUtc });
    }

    [HttpPost("{id:guid}/escalate")]
    public async Task<IActionResult> Escalate(Guid id, AlertActionRequest request, CancellationToken cancellationToken)
    {
        var alert = await dbContext.AlertEvents.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (alert is null)
        {
            return NotFound();
        }

        if (!await IsAuthorizedForCustomerAsync(alert.CustomerId))
        {
            return Forbid();
        }

        alert.ResolutionStatus = AlertResolutionStatus.Escalated;
        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            alert.Notes = request.Notes;
        }

        alert.UpdatedUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        auditLogger.AlertAction("escalate", alert.Id, alert.CustomerId, User.Identity?.Name ?? "unknown");
        return Ok(new { alert.Id, alert.ResolutionStatus, alert.UpdatedUtc });
    }

    [HttpPost("{id:guid}/notes")]
    public async Task<IActionResult> UpdateNotes(Guid id, AlertActionRequest request, CancellationToken cancellationToken)
    {
        var alert = await dbContext.AlertEvents.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (alert is null)
        {
            return NotFound();
        }

        if (!await IsAuthorizedForCustomerAsync(alert.CustomerId))
        {
            return Forbid();
        }

        alert.Notes = request.Notes;
        alert.UpdatedUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        auditLogger.AlertAction("notes", alert.Id, alert.CustomerId, User.Identity?.Name ?? "unknown");
        return Ok(new { alert.Id, alert.Notes, alert.UpdatedUtc });
    }

    private async Task<bool> IsAuthorizedToRaiseAsync(
        string externalDeviceId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        if (Request.Headers.ContainsKey("Authorization") && User.Identity?.IsAuthenticated == true)
        {
            return userAccessService.CanAccessCustomer(User, customerId);
        }

        if (!Request.Headers.TryGetValue(DeviceIngestHeader, out var headerValue))
        {
            return false;
        }

        return await deviceIngestAuthService.IsAuthorizedAsync(
            externalDeviceId,
            headerValue.ToString(),
            cancellationToken);
    }

    private async Task<Guid?> ResolveCustomerIdByExternalDeviceIdAsync(string externalDeviceId, CancellationToken cancellationToken)
    {
        var device = await dbContext.Devices
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.ExternalDeviceId == externalDeviceId && x.IsActive, cancellationToken);
        return device?.CustomerId;
    }

    private async Task<bool> IsAuthorizedForCustomerAsync(Guid customerId)
    {
        var auth = await authorizationService.AuthorizeAsync(User, customerId, AuthPolicies.CustomerScope);
        return auth.Succeeded;
    }
}
