using ScamAlert.Api.Contracts;
using ScamAlert.Api.Services.Billing;

namespace ScamAlert.Api.Tests;

public sealed class BillingAddressValidatorTests
{
    [Fact]
    public void Valid_us_address_passes()
    {
        Assert.True(BillingAddressValidator.TryValidate(
            new UpdateBillingAddressRequest("123 Main St", null, "Wilmington", "DE", "19801"),
            out _));
    }

    [Fact]
    public void Invalid_zip_fails()
    {
        Assert.False(BillingAddressValidator.TryValidate(
            new UpdateBillingAddressRequest("123 Main St", null, "Wilmington", "DE", "ABC"),
            out var error));
        Assert.Contains("ZIP", error, StringComparison.OrdinalIgnoreCase);
    }
}
