using ScamAlert.Api.Services.Auth;

namespace ScamAlert.Api.Tests;

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("LongPassw0rd!")]
    [InlineData("Abcd1234!@")]
    public void Valid_passwords_pass(string password)
    {
        Assert.True(PasswordPolicy.IsValid(password));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short1!A")]
    [InlineData("alllowercase1!")]
    [InlineData("NoDigitsHere!")]
    [InlineData("NoSpecial123")]
    public void Invalid_passwords_fail(string password)
    {
        Assert.False(PasswordPolicy.IsValid(password));
    }
}
