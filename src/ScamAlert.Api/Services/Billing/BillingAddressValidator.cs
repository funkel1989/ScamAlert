using System.Text.RegularExpressions;
using ScamAlert.Api.Contracts;

namespace ScamAlert.Api.Services.Billing;

public static class BillingAddressValidator
{
    private static readonly Regex UsPostal = new(@"^\d{5}(-\d{4})?$", RegexOptions.CultureInvariant);

    public static bool TryValidate(UpdateBillingAddressRequest request, out string error)
    {
        if (string.IsNullOrWhiteSpace(request.Line1))
        {
            error = "Street address is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.City))
        {
            error = "City is required.";
            return false;
        }

        var state = request.State?.Trim() ?? string.Empty;
        if (state.Length is not 2)
        {
            error = "State must be a two-letter code (e.g. DE).";
            return false;
        }

        var postal = request.PostalCode?.Trim() ?? string.Empty;
        if (!UsPostal.IsMatch(postal))
        {
            error = "ZIP code must be 5 digits or ZIP+4 (e.g. 19801 or 19801-1234).";
            return false;
        }

        var country = (request.Country ?? "US").Trim().ToUpperInvariant();
        if (country.Length is not 2)
        {
            error = "Country must be a two-letter code.";
            return false;
        }

        if (!string.Equals(country, "US", StringComparison.Ordinal))
        {
            error = "Only US billing addresses are supported at this time.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
