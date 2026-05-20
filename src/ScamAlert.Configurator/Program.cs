namespace ScamAlert.Configurator;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var pairing = new PairingSetupService(httpClient);

        Application.Run(new MainForm(pairing));
    }
}
