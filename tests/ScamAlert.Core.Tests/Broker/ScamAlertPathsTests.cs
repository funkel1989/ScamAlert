using ScamAlert.Broker.Configuration;

namespace ScamAlert.Core.Tests.Broker;

public sealed class ScamAlertPathsTests
{
    [Fact]
    public void PathsUseScamAlertLocalApplicationDataDirectory()
    {
        var expectedDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScamAlert");

        Assert.Equal(expectedDataDirectory, ScamAlertPaths.DataDirectory);
        Assert.Equal(Path.Combine(expectedDataDirectory, "signals.jsonl"), ScamAlertPaths.SignalFile);
        Assert.Equal(Path.Combine(expectedDataDirectory, "settings.json"), ScamAlertPaths.SettingsFile);
        Assert.Equal(Path.Combine(expectedDataDirectory, "remembered-rules.json"), ScamAlertPaths.RulesFile);
    }
}
