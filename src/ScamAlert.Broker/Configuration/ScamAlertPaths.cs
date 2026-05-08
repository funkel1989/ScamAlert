namespace ScamAlert.Broker.Configuration;

public static class ScamAlertPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScamAlert");

    public static string SignalFile { get; } = Path.Combine(DataDirectory, "signals.jsonl");

    public static string SettingsFile { get; } = Path.Combine(DataDirectory, "settings.json");

    public static string RulesFile { get; } = Path.Combine(DataDirectory, "remembered-rules.json");

    public static string CloudAlertDedupeStateFile { get; } = Path.Combine(DataDirectory, "cloud-alert-dedupe.json");

    public static string CloudAlertPendingOutboxFile { get; } = Path.Combine(DataDirectory, "cloud-alerts-pending.jsonl");

    public static string CloudAlertDeadLetterFile { get; } = Path.Combine(DataDirectory, "cloud-alerts-deadletter.jsonl");
}
