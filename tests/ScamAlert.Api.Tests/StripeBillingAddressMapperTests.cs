using ScamAlert.Api.Services.Stripe;
using ScamAlert.Data.Entities;

namespace ScamAlert.Api.Tests;

public sealed class StripeBillingAddressMapperTests
{
    [Fact]
    public void TryApplyFromStripeAddress_maps_fields()
    {
        var customer = new Customer { Id = Guid.NewGuid() };
        var synced = new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);
        var address = new global::Stripe.Address
        {
            Line1 = "123 Main St",
            Line2 = "Apt 4",
            City = "Wilmington",
            State = "DE",
            PostalCode = "19801",
            Country = "US"
        };

        Assert.True(StripeBillingAddressMapper.TryApplyFromStripeAddress(customer, address, synced));

        Assert.Equal("123 Main St", customer.BillingLine1);
        Assert.Equal("Apt 4", customer.BillingLine2);
        Assert.Equal("Wilmington", customer.BillingCity);
        Assert.Equal("DE", customer.BillingState);
        Assert.Equal("19801", customer.BillingPostalCode);
        Assert.Equal("US", customer.BillingCountry);
        Assert.Equal(synced, customer.BillingAddressSyncedUtc);
    }
}
