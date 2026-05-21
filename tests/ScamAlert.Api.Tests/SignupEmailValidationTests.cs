using System.Net;
using System.Net.Http.Json;

namespace ScamAlert.Api.Tests;

public sealed class SignupEmailValidationTests
{
    [Fact]
    public async Task Signup_invalid_email_returns_bad_request()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/signup",
            new
            {
                name = "Bad Email",
                email = "not-valid",
                password = "LongPassw0rd!",
                planCode = "pro",
                contacts = new[] { new { fullName = "Self", phoneNumber = "+15555550100", escalationOrder = 1 } },
                devices = new[] { new { deviceName = "PC", externalDeviceId = "device-bad-email-1" } },
                consents = SignupTestHelpers.SignupConsentsJson()
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
