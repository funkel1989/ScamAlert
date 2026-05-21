using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScamAlert.Api.Contracts;
using ScamAlert.Data;

namespace ScamAlert.Api.Tests;

public sealed class SignupConsentsTests
{
    [Fact]
    public async Task Signup_persists_consent_timestamps()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/signup",
            new
            {
                name = "Consent Test",
                email = "consent-test@example.com",
                password = "LongPassw0rd!",
                planCode = "pro",
                contacts = new[] { new { fullName = "Self", phoneNumber = "+15555550188", escalationOrder = 1 } },
                devices = new[] { new { deviceName = "PC", externalDeviceId = "device-consent-1" } },
                consents = SignupTestHelpers.SignupConsentsJson()
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ScamAlertDbContext>();
        var customer = await db.Customers.AsNoTracking()
            .SingleAsync(x => x.Email == "consent-test@example.com");

        Assert.NotNull(customer.TermsAcceptedUtc);
        Assert.NotNull(customer.PrivacyAcceptedUtc);
        Assert.NotNull(customer.SmsConsentAcceptedUtc);
        Assert.NotNull(customer.InstallPermissionConfirmedUtc);
        Assert.Equal("2026-05-19", customer.SignupLegalDocumentVersion);
    }

    [Fact]
    public async Task Signup_without_consents_returns_bad_request()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/signup",
            new
            {
                name = "No Consent",
                email = "no-consent@example.com",
                password = "LongPassw0rd!",
                planCode = "pro",
                contacts = new[] { new { fullName = "Self", phoneNumber = "+15555550187", escalationOrder = 1 } },
                devices = new[] { new { deviceName = "PC", externalDeviceId = "device-noconsent-1" } },
                consents = new
                {
                    acceptTerms = false,
                    acceptPrivacy = true,
                    acceptSmsConsent = true,
                    confirmInstallPermission = true
                }
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
