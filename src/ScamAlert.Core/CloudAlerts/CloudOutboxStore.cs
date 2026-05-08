using System.Text.Json;
using Microsoft.Extensions.Logging;
using ScamAlert.Contracts;

namespace ScamAlert.Core.CloudAlerts;

public sealed class CloudOutboxStore(
    string pendingFilePath,
    string deadLetterFilePath,
    ILogger<CloudOutboxStore> logger)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task EnqueueAsync(CloudAlertOutboxItem item, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);

        try
        {
            var directory = Path.GetDirectoryName(pendingFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(item, SignalJson.Options);
            await File.AppendAllTextAsync(pendingFilePath, json + Environment.NewLine, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CloudAlertOutboxItem>> ReadPendingAsync(CancellationToken cancellationToken)
    {
        string text;
        await gate.WaitAsync(cancellationToken);

        try
        {
            text = !File.Exists(pendingFilePath)
                ? string.Empty
                : await File.ReadAllTextAsync(pendingFilePath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }

        var lines = text.Split(
            new[] { "\r\n", "\n", "\r" },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var items = new List<CloudAlertOutboxItem>();
        var badLines = new List<string>();
        foreach (var line in lines)
        {
            try
            {
                var item = JsonSerializer.Deserialize<CloudAlertOutboxItem>(line, SignalJson.Options);
                if (item is not null)
                {
                    items.Add(item);
                }
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Skipping malformed outbox line.");
                badLines.Add(line);
            }
        }

        foreach (var bad in badLines)
        {
            await AppendDeadLetterAsync(bad, "deserialize", cancellationToken).ConfigureAwait(false);
        }

        return items;
    }

    public async Task ReplacePendingAsync(IReadOnlyList<CloudAlertOutboxItem> items, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);

        try
        {
            var directory = Path.GetDirectoryName(pendingFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (items.Count == 0)
            {
                if (File.Exists(pendingFilePath))
                {
                    File.Delete(pendingFilePath);
                }

                return;
            }

            var tempPath = pendingFilePath + ".tmp";
            await using (var writer = new StreamWriter(tempPath, false))
            {
                foreach (var item in items)
                {
                    var json = JsonSerializer.Serialize(item, SignalJson.Options);
                    await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
                }
            }

            File.Move(tempPath, pendingFilePath, overwrite: true);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AppendDeadLetterAsync(string payload, string reason, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);

        try
        {
            var directory = Path.GetDirectoryName(deadLetterFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var envelope = JsonSerializer.Serialize(
                new { reason, at = DateTimeOffset.UtcNow, payload },
                SignalJson.Options);
            await File.AppendAllTextAsync(deadLetterFilePath, envelope + Environment.NewLine, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
}
