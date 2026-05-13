using ScamAlert.Api.Services.Billing;
using ScamAlert.Data.Entities;
using ScamAlert.Data.Enums;

namespace ScamAlert.Api.Services.Stripe;

public static class StripeSubscriptionMapper
{
    public static void MapToLocal(Subscription local, global::Stripe.Subscription stripeSub, IBillingTierCatalog billingTierCatalog)
    {
        local.StripeSubscriptionId = stripeSub.Id;
        var priceId = stripeSub.Items?.Data?.FirstOrDefault()?.Price?.Id;
        if (!string.IsNullOrEmpty(priceId))
        {
            local.StripePriceId = priceId;
        }

        if (stripeSub.Metadata != null &&
            stripeSub.Metadata.TryGetValue("plan_code", out var planCode) &&
            !string.IsNullOrWhiteSpace(planCode))
        {
            local.PlanCode = planCode.Trim();
        }
        else if (!string.IsNullOrEmpty(priceId) &&
                 billingTierCatalog.TryGetPlanCodeForStripePriceId(priceId) is { } mappedPlan)
        {
            local.PlanCode = mappedPlan;
        }

        local.Status = MapStripeStatus(stripeSub.Status);
        local.EndsUtc = TryGetCurrentPeriodEndUtc(stripeSub);
        local.UpdatedUtc = DateTimeOffset.UtcNow;
    }

    public static DateTimeOffset? TryGetCurrentPeriodEndUtc(global::Stripe.Subscription stripeSub)
    {
        var item = stripeSub.Items?.Data?.FirstOrDefault();
        if (item is null)
        {
            return null;
        }

        var end = item.CurrentPeriodEnd;
        if (end == default)
        {
            return null;
        }

        return end.Kind == DateTimeKind.Utc
            ? new DateTimeOffset(end)
            : new DateTimeOffset(DateTime.SpecifyKind(end, DateTimeKind.Utc));
    }

    public static SubscriptionStatus MapStripeStatus(string stripeStatus) =>
        (stripeStatus ?? string.Empty).ToLowerInvariant() switch
        {
            "active" => SubscriptionStatus.Active,
            "trialing" => SubscriptionStatus.Trial,
            "past_due" => SubscriptionStatus.PastDue,
            "canceled" or "unpaid" or "incomplete_expired" => SubscriptionStatus.Canceled,
            "incomplete" => SubscriptionStatus.PendingPayment,
            "paused" => SubscriptionStatus.PastDue,
            _ => SubscriptionStatus.PastDue
        };
}
