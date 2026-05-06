using System.Text.Json;
using ScamAlert.Contracts;

namespace ScamAlert.Core.Configuration;

public sealed class FileProtectionSettingsStore : IProtectionSettingsStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string path;

    public FileProtectionSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        this.path = path;
    }

    public async Task<ProtectionSettings> GetAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);

        try
        {
            if (!File.Exists(path))
            {
                return ProtectionSettings.Default;
            }

            var json = await File.ReadAllTextAsync(path, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                return ProtectionSettings.Default;
            }

            try
            {
                return JsonSerializer.Deserialize<ProtectionSettings>(json, SignalJson.Options)
                    ?? ProtectionSettings.Default;
            }
            catch (JsonException)
            {
                return ProtectionSettings.Default;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(ProtectionSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await gate.WaitAsync(cancellationToken);

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, SignalJson.Options);
            await File.WriteAllTextAsync(path, json, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }
}
