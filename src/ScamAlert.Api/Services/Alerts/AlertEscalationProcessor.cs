using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScamAlert.Data;
using ScamAlert.Data.Entities;
using ScamAlert.Data.Enums;

namespace ScamAlert.Api.Services.Alerts;

public sealed class AlertEscalationProcessor(
    ScamAlertDbContext dbContext,
    AlertContactNotifier contactNotifier,
    IOptions<AlertsOptions> options)
{
    public async Task ProcessDueAlertsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(Math.Max(1, options.Value.EscalationDelaySeconds));

        var pendingAlerts = await dbContext.AlertEvents
            .Include(x => x.NotificationAttempts)
            .Include(x => x.Device)
            .ThenInclude(d => d.Customer)
            .ThenInclude(c => c.Contacts)
            .Where(x => x.ResolutionStatus == AlertResolutionStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var alert in pendingAlerts)
        {
            await TryEscalateAlertAsync(alert, now, delay, cancellationToken);
        }
    }

    private async Task TryEscalateAlertAsync(
        AlertEvent alert,
        DateTimeOffset now,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        var attempts = alert.NotificationAttempts.OrderByDescending(x => x.AttemptedUtc).ToList();
        if (attempts.Count == 0)
        {
            return;
        }

        if (attempts.Any(x => x.Outcome == NotificationOutcome.Acknowledged))
        {
            return;
        }

        var latest = attempts.MaxBy(x => x.AttemptedUtc)!;
        if (now - latest.AttemptedUtc < delay)
        {
            return;
        }

        var contacts = alert.Device.Customer.Contacts
            .Where(x => x.IsActive)
            .OrderBy(x => x.EscalationOrder)
            .ToList();

        var notifiedIds = alert.NotificationAttempts.Select(x => x.ContactId).ToHashSet();
        var remaining = contacts.Where(c => !notifiedIds.Contains(c.Id)).ToList();
        if (remaining.Count == 0)
        {
            alert.ResolutionStatus = AlertResolutionStatus.Escalated;
            alert.UpdatedUtc = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var nextOrder = remaining[0].EscalationOrder;
        var tier = remaining.Where(c => c.EscalationOrder == nextOrder).ToList();

        foreach (var contact in tier)
        {
            if (await contactNotifier.TryNotifyAsync(alert, contact, null, now, cancellationToken))
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
        }

        alert.UpdatedUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
