namespace ScamAlert.Data.Entities;

public sealed class MonitoredDevice
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string ExternalDeviceId { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    public Customer Customer { get; set; } = null!;
    public List<AlertEvent> AlertEvents { get; set; } = [];
}
