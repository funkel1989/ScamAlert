namespace ScamAlert.Broker.Configuration;

public static class ScamAlertPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScamAlert");

    public static string SignalFile { get; } = Path.Combine(DataDirectory, "signals.jsonl");

    public static string SettingsFile { get; } = Path.Combine(DataDirectory, "settings.json");

    public static string RulesFile { get; } = Path.Combine(DataDirectory, "remembered-rules.json");
}
