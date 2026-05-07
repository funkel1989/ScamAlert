namespace ScamAlert.Api.Services.Notifications;

public sealed class LoggingNotificationGateway(ILogger<LoggingNotificationGateway> logger) : INotificationGateway
{
    public Task<GatewayResult> NotifyContactAsync(ContactNotification notification, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<GatewayResult>(cancellationToken);
        }

        logger.LogInformation(
            "Notify contact {ContactId} ({PhoneNumber}) for alert {AlertId}: {Message}",
            notification.ContactId,
            notification.PhoneNumber,
            notification.AlertId,
            notification.Message);

        return Task.FromResult(new GatewayResult(false, null, "No live provider configured."));
    }
}
