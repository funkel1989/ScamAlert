namespace ScamAlert.Api.Contracts;

public sealed record BillingTierSummaryDto(string PlanCode, string DisplayName);

public sealed record BillingAddressDto(
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string Country);

public sealed record BillingSummaryResponse(
    Guid CustomerId,
    string? StripeCustomerId,
    string PlanCode,
    string SubscriptionStatus,
    DateTimeOffset? PeriodEndUtc,
    bool PortalAvailable,
    string? PortalUnavailableReason,
    IReadOnlyList<BillingTierSummaryDto> AvailableTiers,
    string MarketRegion,
    BillingAddressDto? BillingAddress,
    DateTimeOffset? BillingAddressSyncedUtc);

public sealed record UpdateBillingAddressRequest(
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string Country = "US");

public sealed record ChangePlanRequest(string PlanCode);

public sealed record CustomerPortalUrlResponse(string Url);
