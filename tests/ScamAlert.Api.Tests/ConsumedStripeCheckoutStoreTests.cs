using ScamAlert.Api.Services.Signup;

namespace ScamAlert.Api.Tests;

public sealed class ConsumedStripeCheckoutStoreTests
{
    [Fact]
    public void TryMarkConsumed_allows_first_use_only()
    {
        var store = new ConsumedStripeCheckoutStore();
        Assert.True(store.TryMarkConsumed("cs_test_123"));
        Assert.False(store.TryMarkConsumed("cs_test_123"));
    }
}
