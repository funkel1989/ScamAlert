using System.Text.Json;
using Microsoft.Extensions.Logging;
using ScamAlert.Contracts;

namespace ScamAlert.Core.CloudAlerts;

public sealed class FileBackedAlertDeduper(
    string stateFilePath,
    ICloudAlertEnqueueSource enqueueSource,
    ILogger<FileBackedAlertDeduper> logger)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>Returns true if this attempt should be enqueued (first in dedupe window).</summary>
    public async Task<bool> TryReserveEnqueueSlotAsync(
        string dedupeKey,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);

        try
        {
            var snapshot = enqueueSource.GetSnapshot();
            if (!snapshot.Enabled || string.IsNullOrWhiteSpace(snapshot.ExternalDeviceId))
            {
                return false;
            }

            var window = snapshot.DedupeWindow <= TimeSpan.Zero ? TimeSpan.FromMinutes(10) : snapshot.DedupeWindow;

            var state = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
            PruneOldEntries(state, now, window);

            if (state.Entries.TryGetValue(dedupeKey, out var lastUtc) && now - lastUtc < window)
            {
                return false;
            }

            state.Entries[dedupeKey] = now;
            await WriteStateAsync(state, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Deduper failed; allowing enqueue so alerts are not dropped.");
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    private static void PruneOldEntries(DedupeState state, DateTimeOffset now, TimeSpan window)
    {
        var cutoff = now - (window + window);
        foreach (var key in state.Entries.Keys.ToList())
        {
            if (state.Entries[key] < cutoff)
            {
                state.Entries.Remove(key);
            }
        }
    }

    private async Task<DedupeState> ReadStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(stateFilePath))
        {
            return new DedupeState();
        }

        try
        {
            await using var stream = File.OpenRead(stateFilePath);
            var state = await JsonSerializer.DeserializeAsync<DedupeState>(stream, SignalJson.Options, cancellationToken)
                .ConfigureAwait(false);
            return state ?? new DedupeState();
        }
        catch (JsonException)
        {
            return new DedupeState();
        }
    }

    private async Task WriteStateAsync(DedupeState state, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(stateFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = stateFilePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, SignalJson.Options, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, stateFilePath, overwrite: true);
    }

    private sealed class DedupeState
    {
        public Dictionary<string, DateTimeOffset> Entries { get; set; } = new(StringComparer.Ordinal);
    }
}
