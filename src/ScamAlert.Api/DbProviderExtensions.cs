namespace ScamAlert.Api;

internal static class DbProviderExtensions
{
    internal static bool IsSqliteConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        var trimmed = connectionString.TrimStart();
        return trimmed.Contains("Data Source=", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("Filename=", StringComparison.OrdinalIgnoreCase);
    }
}
