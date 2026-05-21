namespace ScamAlert.Data.Entities;

public sealed class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    /// <summary>Stripe Customer id (<c>cus_</c>) for billing portal and recurring charges.</summary>
    public string? StripeCustomerId { get; set; }

    public string? BillingLine1 { get; set; }
    public string? BillingLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountry { get; set; }
    public DateTimeOffset? BillingAddressSyncedUtc { get; set; }

    public DateTimeOffset? TermsAcceptedUtc { get; set; }
    public DateTimeOffset? PrivacyAcceptedUtc { get; set; }
    public DateTimeOffset? SmsConsentAcceptedUtc { get; set; }
    public DateTimeOffset? InstallPermissionConfirmedUtc { get; set; }
    public string? SignupConsentIpAddress { get; set; }
    public string? SignupLegalDocumentVersion { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    public List<Contact> Contacts { get; set; } = [];
    public List<MonitoredDevice> Devices { get; set; } = [];
    public List<Subscription> Subscriptions { get; set; } = [];
    public List<AlertEvent> AlertEvents { get; set; } = [];
}
