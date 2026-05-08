using ScamAlert.Api.Services.Notifications;
using ScamAlert.Data;
using ScamAlert.Data.Entities;
using ScamAlert.Data.Enums;

namespace ScamAlert.Api.Services.Alerts;

public sealed class AlertContactNotifier(
    ScamAlertDbContext dbContext,
    INotificationGateway notificationGateway)
{
    /// <summary>Creates a notification attempt, sends the gateway notification, and updates the attempt. Returns true if the alert should be treated as acknowledged.</summary>
    public async Task<bool> TryNotifyAsync(
        AlertEvent alert,
        Contact contact,
        int? simulateAcknowledgeAtEscalationOrder,
        DateTimeOffset attemptedUtc,
        CancellationToken cancellationToken)
    {
        var attempt = new NotificationAttempt
        {
            Id = Guid.NewGuid(),
            AlertEventId = alert.Id,
            ContactId = contact.Id,
            Channel = "twilio",
            Outcome = NotificationOutcome.NoResponse,
            AcknowledgmentToken = CreateAckToken(),
            AttemptedUtc = attemptedUtc
        };
        dbContext.NotificationAttempts.Add(attempt);

        var gatewayResult = await notificationGateway.NotifyContactAsync(
            new ContactNotification(
                attempt.Id,
                alert.Id,
                contact.Id,
                contact.FullName,
                contact.PhoneNumber,
                $"Suspicious remote-access attempt from {alert.SourceIp} to port {alert.DestinationPort} ({alert.Service}).",
                attempt.AcknowledgmentToken!),
            cancellationToken);

        var simulatedAcknowledged = simulateAcknowledgeAtEscalationOrder == contact.EscalationOrder;
        var acknowledged = gatewayResult.Acknowledged || simulatedAcknowledged;

        attempt.Outcome = acknowledged ? NotificationOutcome.Acknowledged : NotificationOutcome.NoResponse;
        attempt.ProviderMessageId = gatewayResult.ProviderMessageId;
        attempt.Notes = gatewayResult.Notes;
        attempt.AcknowledgedUtc = acknowledged ? attemptedUtc : null;

        if (acknowledged)
        {
            alert.AcknowledgedByContactId = contact.Id;
            alert.ResolutionStatus = AlertResolutionStatus.Acknowledged;
            alert.UpdatedUtc = attemptedUtc;
        }

        return acknowledged;
    }

    private static string CreateAckToken()
    {
        return Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
    }
}
