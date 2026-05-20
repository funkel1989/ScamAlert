namespace ScamAlert.Configurator;

internal static class BrokerConfigPaths
{
    public static string ProgramDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ScamAlert");

    public static string BrokerAppSettingsFile { get; } = Path.Combine(
        ProgramDataDirectory,
        "broker.appsettings.json");

    public const string BrokerServiceName = "ScamAlertBroker";

    public const string DefaultApiBaseUrlRegistryKey = @"SOFTWARE\ScamAlert";
    public const string DefaultApiBaseUrlRegistryValue = "ApiBaseUrl";
}
