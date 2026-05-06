using Microsoft.EntityFrameworkCore;
using ScamAlert.Api.Contracts;
using ScamAlert.Api.Services.Notifications;
using ScamAlert.Data;
using ScamAlert.Data.Entities;
using ScamAlert.Data.Enums;

namespace ScamAlert.Api.Services.Alerts;

public sealed class AlertWorkflowService(
    ScamAlertDbContext dbContext,
    INotificationGateway notificationGateway)
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

        var activeSubscription = device.Customer.Subscriptions.Any(x =>
            x.Status is SubscriptionStatus.Active or SubscriptionStatus.Trial &&
            x.StartsUtc <= DateTimeOffset.UtcNow &&
            (x.EndsUtc is null || x.EndsUtc >= DateTimeOffset.UtcNow));

        if (!activeSubscription)
        {
            throw new InvalidOperationException($"Customer '{device.CustomerId}' has no active or trial subscription.");
        }

        var now = DateTimeOffset.UtcNow;
        var alert = new AlertEvent
        {
            Id = Guid.NewGuid(),
            CustomerId = device.CustomerId,
            DeviceId = device.Id,
            SourceIp = request.SourceIp,
            DestinationPort = request.DestinationPort,
            Service = request.Service,
            ResolutionStatus = AlertResolutionStatus.Pending,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        dbContext.AlertEvents.Add(alert);

        var contacts = device.Customer.Contacts
            .Where(x => x.IsActive)
            .OrderBy(x => x.EscalationOrder)
            .ToList();

        foreach (var contact in contacts)
        {
            var gatewayResult = await notificationGateway.NotifyContactAsync(
                new ContactNotification(
                    alert.Id,
                    contact.Id,
                    contact.FullName,
                    contact.PhoneNumber,
                    $"Suspicious remote-access attempt from {request.SourceIp} to port {request.DestinationPort} ({request.Service})."),
                cancellationToken);

            var simulatedAcknowledged = request.SimulateAcknowledgeAtEscalationOrder == contact.EscalationOrder;
            var acknowledged = gatewayResult.Acknowledged || simulatedAcknowledged;

            dbContext.NotificationAttempts.Add(new NotificationAttempt
            {
                Id = Guid.NewGuid(),
                AlertEventId = alert.Id,
                ContactId = contact.Id,
                Channel = "twilio",
                Outcome = acknowledged ? NotificationOutcome.Acknowledged : NotificationOutcome.NoResponse,
                ProviderMessageId = gatewayResult.ProviderMessageId,
                Notes = gatewayResult.Notes,
                AttemptedUtc = DateTimeOffset.UtcNow
            });

            if (acknowledged)
            {
                alert.AcknowledgedByContactId = contact.Id;
                alert.ResolutionStatus = AlertResolutionStatus.Acknowledged;
                alert.UpdatedUtc = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
                return alert;
            }
        }

        alert.ResolutionStatus = contacts.Count > 0
            ? AlertResolutionStatus.Escalated
            : AlertResolutionStatus.TimedOut;
        alert.UpdatedUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return alert;
    }
}
