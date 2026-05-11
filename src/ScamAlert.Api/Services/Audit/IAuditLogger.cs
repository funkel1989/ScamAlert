namespace ScamAlert.Api.Services.Audit;

public interface IAuditLogger
{
    void AuthTokenIssued(string username, string? ipAddress);
    void AuthFailed(string username, bool lockedOut, string? ipAddress);
    void AlertRaised(Guid alertId, Guid customerId, Guid deviceId, string mode, string? ipAddress);
    void AlertAction(string action, Guid alertId, Guid customerId, string actor);
    void WebhookRejected(string provider, string endpoint, string? ipAddress);
}

public sealed class AuditLogger(ILogger<AuditLogger> logger) : IAuditLogger
{
    public void AuthTokenIssued(string username, string? ipAddress)
        => logger.LogInformation("AUDIT auth token issued user={Username} ip={Ip}", username, ipAddress ?? "unknown");

    public void AuthFailed(string username, bool lockedOut, string? ipAddress)
        => logger.LogWarning(
            "AUDIT auth failed user={Username} lockedOut={LockedOut} ip={Ip}",
            username,
            lockedOut,
            ipAddress ?? "unknown");

    public void AlertRaised(Guid alertId, Guid customerId, Guid deviceId, string mode, string? ipAddress)
        => logger.LogInformation(
            "AUDIT alert raised alertId={AlertId} customerId={CustomerId} deviceId={DeviceId} mode={Mode} ip={Ip}",
            alertId,
            customerId,
            deviceId,
            mode,
            ipAddress ?? "unknown");

    public void AlertAction(string action, Guid alertId, Guid customerId, string actor)
        => logger.LogInformation(
            "AUDIT alert action action={Action} alertId={AlertId} customerId={CustomerId} actor={Actor}",
            action,
            alertId,
            customerId,
            actor);

    public void WebhookRejected(string provider, string endpoint, string? ipAddress)
        => logger.LogWarning(
            "AUDIT webhook rejected provider={Provider} endpoint={Endpoint} ip={Ip}",
            provider,
            endpoint,
            ipAddress ?? "unknown");
}
