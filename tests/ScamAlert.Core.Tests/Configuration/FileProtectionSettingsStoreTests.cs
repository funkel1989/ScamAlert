using ScamAlert.Contracts;
using ScamAlert.Core.Configuration;

namespace ScamAlert.Core.Tests.Configuration;

public sealed class FileProtectionSettingsStoreTests
{
    [Fact]
    public async Task GetAsyncReturnsDefaultWhenSettingsFileDoesNotExist()
    {
        var store = new FileProtectionSettingsStore(CreatePath());

        var settings = await store.GetAsync(CancellationToken.None);

        Assert.Equal(ProtectionSettings.Default, settings);
    }

    [Fact]
    public async Task SaveAsyncPersistsConfiguredSettings()
    {
        var path = CreatePath();
        var store = new FileProtectionSettingsStore(path);
        var configured = new ProtectionSettings(TimeoutPolicy.BlockOnTimeout, 15);

        await store.SaveAsync(configured, CancellationToken.None);

        var reloaded = await new FileProtectionSettingsStore(path).GetAsync(CancellationToken.None);
        Assert.Equal(configured, reloaded);
    }

    private static string CreatePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ScamAlert.Tests", Guid.NewGuid().ToString("N"));
        return Path.Combine(directory, "protection-settings.json");
    }
}
