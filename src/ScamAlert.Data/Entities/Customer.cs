namespace ScamAlert.Data.Entities;

public sealed class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    /// <summary>Stripe Customer id (<c>cus_</c>) for billing portal and recurring charges.</summary>
    public string? StripeCustomerId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    public List<Contact> Contacts { get; set; } = [];
    public List<MonitoredDevice> Devices { get; set; } = [];
    public List<Subscription> Subscriptions { get; set; } = [];
    public List<AlertEvent> AlertEvents { get; set; } = [];
}
