using Microsoft.EntityFrameworkCore;
using ScamAlert.Api.Contracts;
using ScamAlert.Data;
using ScamAlert.Data.Entities;
using ScamAlert.Data.Enums;

namespace ScamAlert.Api.Services.Alerts;

public sealed class AlertWorkflowService(
    ScamAlertDbContext dbContext,
    AlertContactNotifier contactNotifier)
{
    public async Task<AlertEvent> RaiseAlertAsync(RaiseAlertRequest request, CancellationToken cancellationToken)
    {
        var device = await dbContext.Devices
            .Include(x => x.Customer)
            .ThenInclude(x => x.Contacts)
            .Include(x => x.Customer)
            .ThenInclude(x => x.Subscriptions)
            .SingleOrDefaultAsync(x => x.ExternalDeviceId == request.ExternalDeviceId && x.IsActive, cancellationToken);

        if (device is null)
        {
            throw new InvalidOperationException($"No active device found for external id '{request.ExternalDeviceId}'.");
        }

        var now = DateTimeOffset.UtcNow;
        var activeSubscription = device.Customer.Subscriptions.Any(x =>
            (x.Status == SubscriptionStatus.Active || x.Status == SubscriptionStatus.Trial) &&
            x.StartsUtc <= now &&
            (x.EndsUtc is null || x.EndsUtc >= now));

        if (!activeSubscription)
        {
            throw new InvalidOperationException($"Customer '{device.CustomerId}' has no active or trial subscription.");
        }

        if (request.ClientEventId is { } clientEventId)
        {
            var existing = await dbContext.AlertEvents
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.DeviceId == device.Id && x.ClientEventId == clientEventId,
                    cancellationToken);

            if (existing is not null)
            {
                return await dbContext.AlertEvents
                    .Include(x => x.NotificationAttempts)
                    .SingleAsync(x => x.Id == existing.Id, cancellationToken);
            }
        }

        var contacts = device.Customer.Contacts
            .Where(x => x.IsActive)
            .OrderBy(x => x.EscalationOrder)
            .ToList();

        var alert = new AlertEvent
        {
            Id = Guid.NewGuid(),
            CustomerId = device.CustomerId,
            DeviceId = device.Id,
            SourceIp = request.SourceIp,
            DestinationPort = request.DestinationPort,
            Service = request.Service,
            DestinationIp = request.DestinationIp,
            Transport = request.Transport,
            Direction = request.Direction,
            ObservedBy = request.ObservedBy,
            RuleApplied = request.RuleApplied,
            DecisionReason = request.DecisionReason,
            Notes = request.Notes,
            ClientEventId = request.ClientEventId,
            ResolutionStatus = AlertResolutionStatus.Pending,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        dbContext.AlertEvents.Add(alert);

        if (contacts.Count == 0)
        {
            alert.ResolutionStatus = AlertResolutionStatus.TimedOut;
            alert.UpdatedUtc = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            return alert;
        }

        var minOrder = contacts[0].EscalationOrder;
        var firstTier = contacts.Where(x => x.EscalationOrder == minOrder).ToList();

        foreach (var contact in firstTier)
        {
            if (await contactNotifier.TryNotifyAsync(
                    alert,
                    contact,
                    request.SimulateAcknowledgeAtEscalationOrder,
                    now,
                    cancellationToken))
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return alert;
            }
        }

        alert.ResolutionStatus = AlertResolutionStatus.Pending;
        alert.UpdatedUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return alert;
    }
}
