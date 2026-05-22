using ScamAlert.Api.Services.Web;

namespace ScamAlert.Api.Tests;

public sealed class ReturnUrlSanitizerTests
{
    [Theory]
    [InlineData("/dashboard", "/dashboard")]
    [InlineData("contacts", "/contacts")]
    [InlineData(null, "/dashboard")]
    public void Allows_safe_relative_paths(string? input, string expected) =>
        Assert.Equal(expected, ReturnUrlSanitizer.Sanitize(input));

    [Theory]
    [InlineData("https://evil.com")]
    [InlineData("//evil.com")]
    [InlineData("http://localhost/dashboard")]
    public void Blocks_absolute_urls(string input) =>
        Assert.Equal("/dashboard", ReturnUrlSanitizer.Sanitize(input));
}
