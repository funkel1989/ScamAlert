using Microsoft.Extensions.Logging;

namespace ScamAlert.Core.Signals;

public sealed class CompositeSignalSink(
    IReadOnlyList<ISignalSink> sinks,
    ILogger<CompositeSignalSink>? logger = null) : ISignalSink
{
    public async Task AppendAsync<TSignal>(TSignal signal, CancellationToken cancellationToken)
    {
        foreach (var sink in sinks)
        {
            try
            {
                await sink.AppendAsync(signal, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (logger is null)
                {
                    continue;
                }

                logger.LogWarning(ex, "Signal sink {SinkType} failed.", sink.GetType().Name);
            }
        }
    }
}
