namespace ScamAlert.Data.Entities;

public sealed class Contact
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public int EscalationOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    public Customer Customer { get; set; } = null!;
    public List<NotificationAttempt> NotificationAttempts { get; set; } = [];
}
