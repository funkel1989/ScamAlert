namespace ScamAlert.Api.Contracts;

public sealed record BillingTierSummaryDto(string PlanCode, string DisplayName);

public sealed record BillingSummaryResponse(
    Guid CustomerId,
    string? StripeCustomerId,
    string PlanCode,
    string SubscriptionStatus,
    DateTimeOffset? PeriodEndUtc,
    bool PortalAvailable,
    string? PortalUnavailableReason,
    IReadOnlyList<BillingTierSummaryDto> AvailableTiers,
    string MarketRegion);

public sealed record ChangePlanRequest(string PlanCode);

public sealed record CustomerPortalUrlResponse(string Url);
