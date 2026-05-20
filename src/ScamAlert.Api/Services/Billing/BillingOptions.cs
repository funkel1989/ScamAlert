namespace ScamAlert.Api.Services.Billing;

public sealed class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>Monthly (or other interval) Stripe Price IDs keyed by <see cref="BillingTierOptions.PlanCode"/>.</summary>
    public List<BillingTierOptions> Tiers { get; set; } = [];

    /// <summary>ISO market region for this deployment (default US-only).</summary>
    public string MarketRegion { get; set; } = "US";

    /// <summary>Stripe Checkout / Customer Portal locale (US English).</summary>
    public string Locale { get; set; } = "en-US";

    /// <summary>When true, Checkout collects a billing address (recommended for US card compliance).</summary>
    public bool RequireBillingAddressOnCheckout { get; set; } = true;
}

public sealed class BillingTierOptions
{
    /// <summary>Matches <see cref="ScamAlert.Data.Entities.Subscription.PlanCode"/> and signup/API plan selection.</summary>
    public string PlanCode { get; set; } = string.Empty;

    /// <summary>Stripe Price id (recurring price from the Stripe Dashboard).</summary>
    public string StripePriceId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Short line for pricing cards (marketing).</summary>
    public string? MarketingSummary { get; set; }

    /// <summary>Displayed price label, e.g. "Billed monthly in USD".</summary>
    public string? MonthlyPriceLabel { get; set; }

    /// <summary>Bullet features for pricing page.</summary>
    public List<string> MarketingFeatures { get; set; } = [];
}
