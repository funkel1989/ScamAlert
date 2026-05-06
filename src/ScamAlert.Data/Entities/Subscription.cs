using ScamAlert.Data.Enums;

namespace ScamAlert.Data.Entities;

public sealed class Subscription
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string PlanCode { get; set; } = string.Empty;
    public SubscriptionStatus Status { get; set; }
    public DateTimeOffset StartsUtc { get; set; }
    public DateTimeOffset? EndsUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    public Customer Customer { get; set; } = null!;
}
