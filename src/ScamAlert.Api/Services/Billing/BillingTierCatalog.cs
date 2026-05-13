using Microsoft.Extensions.Options;

namespace ScamAlert.Api.Services.Billing;

public interface IBillingTierCatalog
{
    string GetStripePriceId(string planCode);

    /// <summary>Returns configured plan code for a Stripe price id, or null if unknown.</summary>
    string? TryGetPlanCodeForStripePriceId(string stripePriceId);
}

public sealed class BillingTierCatalog(IOptions<BillingOptions> options) : IBillingTierCatalog
{
    public string GetStripePriceId(string planCode)
    {
        var normalized = planCode.Trim();
        var tier = options.Value.Tiers.FirstOrDefault(t =>
            string.Equals(t.PlanCode, normalized, StringComparison.OrdinalIgnoreCase));
        if (tier is null || string.IsNullOrWhiteSpace(tier.StripePriceId))
        {
            throw new InvalidOperationException(
                $"Billing tier '{normalized}' is not configured. Add it under Billing:Tiers with a recurring Stripe PriceId.");
        }

        return tier.StripePriceId.Trim();
    }

    public string? TryGetPlanCodeForStripePriceId(string stripePriceId)
    {
        if (string.IsNullOrWhiteSpace(stripePriceId))
        {
            return null;
        }

        var tier = options.Value.Tiers.FirstOrDefault(t =>
            string.Equals(t.StripePriceId, stripePriceId.Trim(), StringComparison.Ordinal));
        return tier?.PlanCode;
    }
}
