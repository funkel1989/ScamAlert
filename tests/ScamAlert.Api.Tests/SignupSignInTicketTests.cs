using System.Net;
using System.Net.Http.Json;

namespace ScamAlert.Api.Tests;

public sealed class SignupSignInTicketTests
{
    [Fact]
    public async Task Signup_complete_with_valid_ticket_redirects_to_dashboard()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var signup = await client.PostAsJsonAsync(
            "/api/signup",
            new
            {
                name = "Ticket Sign In",
                email = "ticket-signin@example.com",
                password = "LongPassw0rd!",
                planCode = "pro",
                contacts = new[] { new { fullName = "Self", phoneNumber = "+15555550111", escalationOrder = 1 } },
                devices = new[] { new { deviceName = "PC", externalDeviceId = "device-ticket-1" } },
                consents = SignupTestHelpers.SignupConsentsJson()
            });
        signup.EnsureSuccessStatusCode();
        var doc = await signup.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var completeUrl = doc.GetProperty("checkoutUrl").GetString()!;

        var complete = await client.GetAsync(completeUrl);
        Assert.Equal(HttpStatusCode.Redirect, complete.StatusCode);
        Assert.Equal("/dashboard?welcome=true", complete.Headers.Location?.OriginalString);
    }
}
