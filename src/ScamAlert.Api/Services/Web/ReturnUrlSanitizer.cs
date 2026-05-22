namespace ScamAlert.Api.Services.Web;

public static class ReturnUrlSanitizer
{
    public static string Sanitize(string? returnUrl, string fallback = "/dashboard")
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return fallback;
        }

        var trimmed = returnUrl.Trim();
        if (trimmed.Contains("://", StringComparison.Ordinal) ||
            trimmed.StartsWith("//", StringComparison.Ordinal) ||
            trimmed.StartsWith('\\') ||
            trimmed.Contains('@'))
        {
            return fallback;
        }

        var path = trimmed.StartsWith('/') ? trimmed : $"/{trimmed}";
        if (path.StartsWith("//", StringComparison.Ordinal) || path.Contains(':'))
        {
            return fallback;
        }

        return path;
    }
}
