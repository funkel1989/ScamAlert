namespace ScamAlert.Api.Services.Notifications;

public interface INotificationGateway
{
    Task<GatewayResult> NotifyContactAsync(ContactNotification notification, CancellationToken cancellationToken);
}

public sealed record ContactNotification(
    Guid NotificationAttemptId,
    Guid AlertId,
    Guid ContactId,
    string ContactName,
    string PhoneNumber,
    string Message,
    string AcknowledgmentToken);

public sealed record GatewayResult(
    bool Acknowledged,
    string? ProviderMessageId,
    string? Notes);
