using Microsoft.Extensions.Options;
using ScamAlert.Api.Services.Billing;

namespace ScamAlert.Api.Tests;

public sealed class BillingTierCatalogTests
{
    [Fact]
    public void GetStripePriceId_is_case_insensitive()
    {
        var catalog = new BillingTierCatalog(Options.Create(new BillingOptions
        {
            Tiers = [new BillingTierOptions { PlanCode = "pro", StripePriceId = "price_abc" }]
        }));

        Assert.Equal("price_abc", catalog.GetStripePriceId("PRO"));
    }

    [Fact]
    public void TryGetPlanCodeForStripePriceId_round_trips()
    {
        var catalog = new BillingTierCatalog(Options.Create(new BillingOptions
        {
            Tiers = [new BillingTierOptions { PlanCode = "family", StripePriceId = "price_xyz" }]
        }));

        Assert.Equal("family", catalog.TryGetPlanCodeForStripePriceId("price_xyz"));
    }

    [Fact]
    public void GetStripePriceId_unknown_throws()
    {
        var catalog = new BillingTierCatalog(Options.Create(new BillingOptions { Tiers = [] }));
        Assert.Throws<InvalidOperationException>(() => catalog.GetStripePriceId("enterprise"));
    }
}
