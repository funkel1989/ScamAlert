using System.Text;

namespace ScamAlert.Api.Services.Phone;

/// <summary>US mobile numbers: users type (888) 888-8888; stored/sent to Twilio as E.164 (+1XXXXXXXXXX).</summary>
public static class UsPhoneNumber
{
    public const int MaxDigits = 10;

    /// <summary>Max length of formatted display, e.g. (555) 555-5555.</summary>
    public const int MaxFormattedLength = 14;

    public const string InvalidMessage = "Enter a valid 10-digit U.S. phone number, e.g. (888) 888-8888.";

    public static bool TryNormalize(string? input, out string e164, out string? error)
    {
        e164 = string.Empty;
        error = null;

        var digits = ExtractUsDigits(input);
        if (digits is null)
        {
            error = InvalidMessage;
            return false;
        }

        e164 = $"+1{digits}";
        return true;
    }

    public static string? ExtractUsDigits(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        if (!TryTakeDigits(input, MaxDigits, requireTenDigits: true, out var value))
        {
            return null;
        }

        if (value[0] is '0' or '1')
        {
            return null;
        }

        return value;
    }

    /// <summary>Extracts up to <paramref name="maxDigits"/> digits; strips a leading 1 on 11-digit input.</summary>
    public static string TakeDigits(string? input, int maxDigits = MaxDigits)
    {
        TryTakeDigits(input, maxDigits, requireTenDigits: false, out var digits);
        return digits;
    }

    private static bool TryTakeDigits(
        string? input,
        int maxDigits,
        bool requireTenDigits,
        out string digits)
    {
        digits = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return !requireTenDigits;
        }

        var buffer = new StringBuilder();
        foreach (var ch in input)
        {
            if (char.IsDigit(ch))
            {
                buffer.Append(ch);
            }
        }

        if (buffer.Length == 0)
        {
            return !requireTenDigits;
        }

        var value = buffer.ToString();
        if (value.Length == 11 && value.StartsWith('1'))
        {
            value = value[1..];
        }

        if (value.Length > maxDigits)
        {
            value = value[..maxDigits];
        }

        digits = value;
        return !requireTenDigits || digits.Length == maxDigits;
    }

    public static string FormatDisplay(string? inputOrE164)
    {
        var digits = ExtractUsDigits(inputOrE164);
        if (digits is null)
        {
            return inputOrE164?.Trim() ?? string.Empty;
        }

        return $"({digits[..3]}) {digits[3..6]}-{digits[6..]}";
    }

    public static string FormatAsYouType(string? input)
    {
        var digits = TakeDigits(input, MaxDigits);
        if (string.IsNullOrEmpty(digits))
        {
            return string.Empty;
        }

        return digits.Length switch
        {
            <= 3 => $"({digits}",
            <= 6 => $"({digits[..3]}) {digits[3..]}",
            _ => $"({digits[..3]}) {digits[3..6]}-{digits[6..]}"
        };
    }
}
