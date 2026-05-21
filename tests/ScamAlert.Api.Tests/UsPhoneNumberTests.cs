using ScamAlert.Api.Services.Phone;

namespace ScamAlert.Api.Tests;

public sealed class UsPhoneNumberTests
{
    [Theory]
    [InlineData("(555) 555-0100", "+15555550100")]
    [InlineData("5555550100", "+15555550100")]
    [InlineData("+1 555 555 0100", "+15555550100")]
    [InlineData("1-555-555-0100", "+15555550100")]
    public void TryNormalize_accepts_common_us_formats(string input, string expected)
    {
        Assert.True(UsPhoneNumber.TryNormalize(input, out var e164, out var error));
        Assert.Null(error);
        Assert.Equal(expected, e164);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("(011) 555-0100")]
    public void TryNormalize_rejects_invalid(string input)
    {
        Assert.False(UsPhoneNumber.TryNormalize(input, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void FormatDisplay_formats_e164()
    {
        Assert.Equal("(555) 555-0100", UsPhoneNumber.FormatDisplay("+15555550100"));
    }

    [Fact]
    public void FormatAsYouType_stops_at_ten_digits()
    {
        var formatted = UsPhoneNumber.FormatAsYouType("555555010012345");
        Assert.Equal("(555) 555-0100", formatted);
        Assert.Equal(10, UsPhoneNumber.TakeDigits(formatted).Length);
    }
}
