namespace ScamAlert.Core.Signals;

public interface ISignalSink
{
    Task AppendAsync<TSignal>(TSignal signal, CancellationToken cancellationToken);
}
