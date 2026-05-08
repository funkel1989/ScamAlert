using Microsoft.Extensions.Logging;
using ScamAlert.Contracts;
using ScamAlert.Core.Signals;

namespace ScamAlert.Core.CloudAlerts;

public sealed class ObservedInboundCloudEnqueueSignalSink(
    ICloudAlertEnqueueSource enqueueSource,
    FileBackedAlertDeduper deduper,
    CloudOutboxStore outboxStore,
    ILogger<ObservedInboundCloudEnqueueSignalSink> logger) : ISignalSink
{
    public async Task AppendAsync<TSignal>(TSignal signal, CancellationToken cancellationToken)
    {
        if (signal is not ObservedInboundAttemptSignal observed)
        {
            return;
        }

        try
        {
            var snapshot = enqueueSource.GetSnapshot();
            if (!snapshot.Enabled || string.IsNullOrWhiteSpace(snapshot.ExternalDeviceId))
            {
                return;
            }

            var dedupeKey = $"{snapshot.ExternalDeviceId}|{observed.SourceIp}|{observed.DestinationPort}";
            if (!await deduper.TryReserveEnqueueSlotAsync(dedupeKey, observed.OccurredAt, cancellationToken)
                    .ConfigureAwait(false))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var item = new CloudAlertOutboxItem
            {
                Id = Guid.NewGuid(),
                ClientEventId = observed.EventId,
                ExternalDeviceId = snapshot.ExternalDeviceId,
                SourceIp = observed.SourceIp,
                DestinationPort = observed.DestinationPort,
                Service = ToServiceString(observed.ProtectedService),
                AttemptCount = 0,
                NextAttemptUtc = now,
                EnqueuedUtc = now
            };

            await outboxStore.EnqueueAsync(item, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enqueue cloud alert for observed inbound attempt.");
        }
    }

    private static string ToServiceString(ProtectedService service)
    {
        return service switch
        {
            ProtectedService.Rdp => "rdp",
            ProtectedService.Ssh => "ssh",
            ProtectedService.Telnet => "telnet",
            _ => "unknown"
        };
    }
}
