using ScamAlert.Api.Services.Validation;

namespace ScamAlert.Api.Tests;

public sealed class EmailAddressValidatorTests
{
    [Theory]
    [InlineData("user@example.com")]
    [InlineData("name.last+tag@company.co")]
    public void Valid_emails_pass(string email)
    {
        Assert.True(EmailAddressValidator.IsValid(email));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("missing-at.com")]
    [InlineData("@nodomain.com")]
    public void Invalid_emails_fail(string email)
    {
        Assert.False(EmailAddressValidator.IsValid(email));
    }

    [Fact]
    public void TryValidate_trims_whitespace()
    {
        Assert.True(EmailAddressValidator.TryValidate("  user@example.com  ", out var normalized, out _));
        Assert.Equal("user@example.com", normalized);
    }
}
