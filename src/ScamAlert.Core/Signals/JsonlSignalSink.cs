using System.Text.Json;
using ScamAlert.Contracts;

namespace ScamAlert.Core.Signals;

public sealed class JsonlSignalSink : ISignalSink
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string path;

    public JsonlSignalSink(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        this.path = path;
    }

    public async Task AppendAsync<TSignal>(TSignal signal, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(signal, SignalJson.Options);
            await File.AppendAllTextAsync(path, json + Environment.NewLine, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }
}
