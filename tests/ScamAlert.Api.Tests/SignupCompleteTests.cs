using System.Net;

namespace ScamAlert.Api.Tests;

public sealed class SignupCompleteTests
{
    [Fact]
    public async Task Signup_success_legacy_url_redirects_to_complete()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/signup/success?session_id=cs_test");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/signup/complete?session_id=cs_test", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Signup_complete_without_session_redirects_to_login()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/signup/complete");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
        Assert.Contains("returnUrl=/dashboard", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }
}
