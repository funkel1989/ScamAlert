using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ScamAlert.Api.Tests;

/// <summary>End-to-end HTTP checks for signup/login redirect flows beyond unit tests.</summary>
public sealed class RedirectFlowValidationTests
{
    [Fact]
    public async Task Dev_signup_ticket_post_signs_in_and_redirects_to_dashboard()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = await CreateAntiforgeryClientAsync(factory);

        var email = $"redirect-flow-{Guid.NewGuid():N}@example.com";
        var signup = await client.PostAsJsonAsync(
            "/api/signup",
            new
            {
                name = "Redirect Flow",
                email,
                password = "LongPassw0rd!",
                planCode = "pro",
                contacts = new[] { new { fullName = "Self", phoneNumber = "+15555550100", escalationOrder = 1 } },
                devices = new[] { new { deviceName = "PC", externalDeviceId = $"device-{Guid.NewGuid():N}" } },
                consents = SignupTestHelpers.SignupConsentsJson()
            });
        signup.EnsureSuccessStatusCode();

        var doc = await signup.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("/signup/complete", doc.GetProperty("checkoutUrl").GetString());
        var ticket = doc.GetProperty("signInTicket").GetString();
        Assert.False(string.IsNullOrWhiteSpace(ticket));

        var token = await GetAntiforgeryTokenAsync(client);
        var complete = await PostFormAsync(client, "/signup/complete", token, new Dictionary<string, string?>
        {
            ["ticket"] = ticket
        });
        Assert.Equal(HttpStatusCode.Redirect, complete.StatusCode);
        Assert.Equal("/dashboard?welcome=true", complete.Headers.Location?.OriginalString);

        var replayToken = await GetAntiforgeryTokenAsync(client);
        var replay = await PostFormAsync(client, "/signup/complete", replayToken, new Dictionary<string, string?>
        {
            ["ticket"] = ticket
        });
        Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode);
        Assert.Contains("/login", replay.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_post_with_valid_credentials_redirects_to_dashboard()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = await CreateAntiforgeryClientAsync(factory);

        var email = $"login-flow-{Guid.NewGuid():N}@example.com";
        await SignupUserAsync(client, email);

        var token = await GetAntiforgeryTokenAsync(client);
        var signIn = await PostFormAsync(client, "/login/sign-in", token, new Dictionary<string, string?>
        {
            ["email"] = email,
            ["password"] = "LongPassw0rd!",
            ["returnUrl"] = "/dashboard"
        });
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        Assert.Equal("/dashboard", signIn.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Login_post_blocks_open_redirect_on_returnUrl()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = await CreateAntiforgeryClientAsync(factory);

        var email = $"login-redirect-{Guid.NewGuid():N}@example.com";
        await SignupUserAsync(client, email);

        var token = await GetAntiforgeryTokenAsync(client);
        var signIn = await PostFormAsync(client, "/login/sign-in", token, new Dictionary<string, string?>
        {
            ["email"] = email,
            ["password"] = "LongPassw0rd!",
            ["returnUrl"] = "https://evil.example/phish"
        });
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        Assert.Equal("/dashboard", signIn.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Login_post_honors_safe_relative_returnUrl()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = await CreateAntiforgeryClientAsync(factory);

        var email = $"login-return-{Guid.NewGuid():N}@example.com";
        await SignupUserAsync(client, email);

        var token = await GetAntiforgeryTokenAsync(client);
        var signIn = await PostFormAsync(client, "/login/sign-in", token, new Dictionary<string, string?>
        {
            ["email"] = email,
            ["password"] = "LongPassw0rd!",
            ["returnUrl"] = "/contacts"
        });
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        Assert.Equal("/contacts", signIn.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Legacy_signup_success_and_stripe_complete_redirects_work()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var withSession = await client.GetAsync("/signup/success?session_id=cs_test_abc");
        Assert.Equal(HttpStatusCode.Redirect, withSession.StatusCode);
        Assert.Equal("/signup/complete?session_id=cs_test_abc", withSession.Headers.Location?.OriginalString);

        var bare = await client.GetAsync("/signup/success");
        Assert.Equal("/signup/complete", bare.Headers.Location?.OriginalString);

        var noSession = await client.GetAsync("/signup/complete");
        Assert.Contains("/login", noSession.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    private static async Task<HttpClient> CreateAntiforgeryClientAsync(TestWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        await GetAntiforgeryTokenAsync(client);
        return client;
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/__test/antiforgery-token");
        response.EnsureSuccessStatusCode();
        var doc = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var token = doc.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        return token!;
    }

    private static Task<HttpResponseMessage> PostFormAsync(
        HttpClient client,
        string path,
        string antiforgeryToken,
        Dictionary<string, string?> fields)
    {
        fields["__RequestVerificationToken"] = antiforgeryToken;
        return client.PostAsync(path, new FormUrlEncodedContent(fields!));
    }

    private static async Task SignupUserAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync(
            "/api/signup",
            new
            {
                name = "Login Flow User",
                email,
                password = "LongPassw0rd!",
                planCode = "pro",
                contacts = new[] { new { fullName = "Self", phoneNumber = "+15555550100", escalationOrder = 1 } },
                devices = new[] { new { deviceName = "PC", externalDeviceId = $"device-{Guid.NewGuid():N}" } },
                consents = SignupTestHelpers.SignupConsentsJson()
            });
        response.EnsureSuccessStatusCode();
    }
}
