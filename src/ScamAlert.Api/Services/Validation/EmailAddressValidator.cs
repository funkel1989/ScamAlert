using System.ComponentModel.DataAnnotations;

namespace ScamAlert.Api.Services.Validation;

public static class EmailAddressValidator
{
    public const int MaxLength = 320;

    public const string InvalidMessage = "Enter a valid email address, e.g. you@example.com.";

    private static readonly EmailAddressAttribute Attribute = new();

    public static bool IsValid(string? email) => TryValidate(email, out _, out _);

    public static bool TryValidate(string? email, out string normalized, out string? error)
    {
        normalized = email?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(normalized))
        {
            error = "Email is required.";
            return false;
        }

        if (normalized.Length > MaxLength)
        {
            error = "Email is too long.";
            return false;
        }

        if (!Attribute.IsValid(normalized))
        {
            error = InvalidMessage;
            return false;
        }

        error = null;
        return true;
    }
}
