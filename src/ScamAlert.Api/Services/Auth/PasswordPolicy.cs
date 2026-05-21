using System.Text.RegularExpressions;

namespace ScamAlert.Api.Services.Auth;

public static class PasswordPolicy
{
    public const int MinLength = 10;

    public const string RequirementsDescription =
        "At least 10 characters with an uppercase letter, a number, and a special character.";

    private static readonly Regex Uppercase = new("[A-Z]", RegexOptions.CultureInvariant);
    private static readonly Regex Digit = new("[0-9]", RegexOptions.CultureInvariant);
    private static readonly Regex Special = new(@"[^A-Za-z0-9]", RegexOptions.CultureInvariant);

    public static bool IsValid(string? password) => TryValidate(password, out _);

    public static PasswordRequirementStatus Evaluate(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return new PasswordRequirementStatus(false, false, false, false);
        }

        return new PasswordRequirementStatus(
            password.Length >= MinLength,
            Uppercase.IsMatch(password),
            Digit.IsMatch(password),
            Special.IsMatch(password));
    }

    public static bool TryValidate(string? password, out string error)
    {
        if (string.IsNullOrEmpty(password))
        {
            error = "Password is required.";
            return false;
        }

        if (password.Length < MinLength)
        {
            error = $"Password must be at least {MinLength} characters.";
            return false;
        }

        if (!Uppercase.IsMatch(password))
        {
            error = "Password must include at least one uppercase letter.";
            return false;
        }

        if (!Digit.IsMatch(password))
        {
            error = "Password must include at least one number.";
            return false;
        }

        if (!Special.IsMatch(password))
        {
            error = "Password must include at least one special character.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public sealed record PasswordRequirementStatus(
    bool MinLength,
    bool HasUppercase,
    bool HasDigit,
    bool HasSpecial)
{
    public bool AllMet => MinLength && HasUppercase && HasDigit && HasSpecial;
}
