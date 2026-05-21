using ScamAlert.Api.Contracts;

namespace ScamAlert.Api.Tests;

internal static class SignupTestHelpers
{
    public static SignupConsents ValidConsents() =>
        new(true, true, true, true, "2026-05-19");

    public static object SignupConsentsJson() => new
    {
        acceptTerms = true,
        acceptPrivacy = true,
        acceptSmsConsent = true,
        confirmInstallPermission = true,
        legalDocumentVersion = "2026-05-19"
    };
}
