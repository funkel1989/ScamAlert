using ScamAlert.Api.Contracts;
using ScamAlert.Data.Entities;

namespace ScamAlert.Api.Services.Stripe;

public static class StripeBillingAddressMapper
{
    public static bool TryApplyFromStripeAddress(Customer local, global::Stripe.Address? address, DateTimeOffset syncedUtc)
    {
        if (address is null || string.IsNullOrWhiteSpace(address.Line1))
        {
            return false;
        }

        local.BillingLine1 = Trim(address.Line1, 200);
        local.BillingLine2 = TrimOrNull(address.Line2, 200);
        local.BillingCity = TrimOrNull(address.City, 100);
        local.BillingState = TrimOrNull(address.State, 50);
        local.BillingPostalCode = TrimOrNull(address.PostalCode, 20);
        local.BillingCountry = TrimOrNull(address.Country, 2) ?? "US";
        local.BillingAddressSyncedUtc = syncedUtc;
        local.UpdatedUtc = syncedUtc;
        return true;
    }

    public static void ApplyToLocalCustomer(Customer local, UpdateBillingAddressRequest request, DateTimeOffset syncedUtc)
    {
        local.BillingLine1 = Trim(request.Line1, 200);
        local.BillingLine2 = TrimOrNull(request.Line2, 200);
        local.BillingCity = Trim(request.City, 100);
        local.BillingState = Trim(request.State, 50).ToUpperInvariant();
        local.BillingPostalCode = Trim(request.PostalCode, 20);
        local.BillingCountry = (request.Country ?? "US").Trim().ToUpperInvariant()[..2];
        local.BillingAddressSyncedUtc = syncedUtc;
        local.UpdatedUtc = syncedUtc;
    }

    public static BillingAddressDto? ToDto(Customer local)
    {
        if (string.IsNullOrWhiteSpace(local.BillingLine1))
        {
            return null;
        }

        return new BillingAddressDto(
            local.BillingLine1,
            local.BillingLine2,
            local.BillingCity ?? string.Empty,
            local.BillingState ?? string.Empty,
            local.BillingPostalCode ?? string.Empty,
            local.BillingCountry ?? "US");
    }

    public static async Task PushToStripeCustomerAsync(
        string stripeCustomerId,
        Customer local,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(local.BillingLine1))
        {
            return;
        }

        await new global::Stripe.CustomerService().UpdateAsync(
            stripeCustomerId,
            new global::Stripe.CustomerUpdateOptions
            {
                Address = new global::Stripe.AddressOptions
                {
                    Line1 = local.BillingLine1,
                    Line2 = local.BillingLine2,
                    City = local.BillingCity,
                    State = local.BillingState,
                    PostalCode = local.BillingPostalCode,
                    Country = local.BillingCountry ?? "US"
                }
            },
            cancellationToken: cancellationToken);
    }

    public static async Task<bool> TrySyncFromStripeCustomerAsync(
        Customer local,
        string stripeCustomerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stripeCustomerId))
        {
            return false;
        }

        var stripeCustomer = await new global::Stripe.CustomerService().GetAsync(
            stripeCustomerId,
            cancellationToken: cancellationToken);

        return TryApplyFromStripeAddress(local, stripeCustomer.Address, DateTimeOffset.UtcNow);
    }

    private static string Trim(string value, int max) =>
        value.Trim()[..Math.Min(value.Trim().Length, max)];

    private static string? TrimOrNull(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
