using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ScamAlert.Contracts;

namespace ScamAlert.Broker.Client;

public sealed class BrokerPipeProtocolException : Exception
{
    public BrokerPipeProtocolException(string message) : base(message) { }
    public BrokerPipeProtocolException(string message, Exception inner) : base(message, inner) { }
}

public sealed class BrokerDriverPipeClient
{
    public const string DefaultPipeName = "scamalert-driver-events";
    private static readonly TimeSpan DefaultConnectionTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);

    private readonly string _pipeName;
    private readonly TimeSpan _connectionTimeout;
    private readonly TimeSpan _requestTimeout;

    public BrokerDriverPipeClient()
        : this(DefaultPipeName, DefaultConnectionTimeout, DefaultRequestTimeout) { }

    public BrokerDriverPipeClient(string pipeName)
        : this(pipeName, DefaultConnectionTimeout, DefaultRequestTimeout) { }

    public BrokerDriverPipeClient(string pipeName, TimeSpan requestTimeout)
        : this(pipeName, DefaultConnectionTimeout, requestTimeout) { }

    public BrokerDriverPipeClient(string pipeName, TimeSpan connectionTimeout, TimeSpan requestTimeout)
    {
        ArgumentException.ThrowIfNullOrEmpty(pipeName);
        if (connectionTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(connectionTimeout));
        if (requestTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(requestTimeout));

        _pipeName = pipeName;
        _connectionTimeout = connectionTimeout;
        _requestTimeout = requestTimeout;
    }

    public async Task<DriverDecisionResponse> SendAttemptAsync(
        ProtectedConnectionAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        await using var pipe = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync((int)_connectionTimeout.TotalMilliseconds, cancellationToken);
        }
        catch (TimeoutException ex)
        {
            throw new BrokerPipeProtocolException(
                $"Broker pipe '{_pipeName}' was unavailable within {_connectionTimeout.TotalMilliseconds:F0} ms.",
                ex);
        }

        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(_requestTimeout);
        var requestToken = requestTimeout.Token;

        using var reader = new StreamReader(
            pipe,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);

        await using var writer = new StreamWriter(
            pipe,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            leaveOpen: true)
        {
            AutoFlush = true
        };

        try
        {
            var requestLine = JsonSerializer.Serialize(attempt, SignalJson.Options);
            await writer.WriteLineAsync(requestLine.AsMemory(), requestToken);

            var responseLine = await reader.ReadLineAsync(requestToken);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                throw new BrokerPipeProtocolException("Broker pipe closed without a response.");
            }

            DriverDecisionResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<DriverDecisionResponse>(responseLine, SignalJson.Options);
            }
            catch (JsonException ex)
            {
                throw new BrokerPipeProtocolException("Broker pipe returned malformed JSON.", ex);
            }

            if (response is null)
            {
                throw new BrokerPipeProtocolException("Broker pipe returned an invalid (null) response.");
            }

            if (response.ObservedEventId != attempt.EventId)
            {
                throw new BrokerPipeProtocolException(
                    $"Broker pipe returned a response for event {response.ObservedEventId:D} but the request was for {attempt.EventId:D}.");
            }

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new BrokerPipeProtocolException(
                $"Broker pipe response timed out after {_requestTimeout.TotalSeconds:F0} s.",
                ex);
        }
        catch (IOException ex)
        {
            throw new BrokerPipeProtocolException("Broker pipe IO failure.", ex);
        }
    }
}
