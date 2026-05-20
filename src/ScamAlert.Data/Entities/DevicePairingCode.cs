namespace ScamAlert.Data.Entities;

public sealed class DevicePairingCode
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? RedeemedUtc { get; set; }

    public MonitoredDevice Device { get; set; } = null!;
}
