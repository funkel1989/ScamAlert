using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScamAlert.Contracts;
using ScamAlert.Core.Broker;

namespace ScamAlert.Broker.DriverPipe;

public sealed class NamedPipeDriverServer(
    RemoteAccessBroker broker,
    ILogger<NamedPipeDriverServer> logger) : BackgroundService
{
    public const string PipeName = "scamalert-driver-events";
    private const int MaxFrameBytes = 16 * 1024;
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(30);
    private static readonly UTF8Encoding Utf8NoBomStrict = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(stoppingToken);
                using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                connectionTimeout.CancelAfter(ConnectionTimeout);
                var connectionToken = connectionTimeout.Token;

                await using var writer = new StreamWriter(
                    pipe,
                    Utf8NoBomStrict,
                    leaveOpen: true)
                {
                    AutoFlush = true
                };

                var requestLine = await ReadRequestFrameAsync(pipe, connectionToken);
                if (string.IsNullOrWhiteSpace(requestLine))
                {
                    logger.LogWarning("Driver pipe request was missing or blank.");
                    continue;
                }

                var attempt = JsonSerializer.Deserialize<ProtectedConnectionAttempt>(
                    requestLine,
                    SignalJson.Options);

                if (attempt is null)
                {
                    logger.LogWarning("Driver pipe request could not be deserialized.");
                    continue;
                }

                if (!ValidateAttempt(attempt))
                {
                    logger.LogWarning(
                        "Driver pipe request had invalid protected service mapping. DestinationPort: {DestinationPort}; ProtectedService: {ProtectedService}",
                        attempt.DestinationPort,
                        attempt.ProtectedService);
                    continue;
                }

                var decision = await broker.HandleAttemptAsync(attempt, connectionToken);
                var responseLine = JsonSerializer.Serialize(decision, SignalJson.Options);

                await writer.WriteLineAsync(responseLine.AsMemory(), connectionToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Driver pipe request timed out.");
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Driver pipe request was malformed.");
            }
            catch (DecoderFallbackException ex)
            {
                logger.LogWarning(ex, "Driver pipe request was not valid UTF-8.");
            }
            catch (DriverPipeProtocolException ex)
            {
                logger.LogWarning(ex, "Driver pipe protocol failure.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process driver pipe request.");
            }
        }
    }

    private static async Task<string> ReadRequestFrameAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var frame = new MemoryStream(capacity: MaxFrameBytes);
        var buffer = new byte[1];

        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
            if (bytesRead == 0)
            {
                if (frame.Length == 0)
                {
                    return string.Empty;
                }

                throw new DriverPipeProtocolException("Driver pipe request ended before LF.");
            }

            if (buffer[0] == '\n')
            {
                break;
            }

            if (frame.Length >= MaxFrameBytes)
            {
                throw new DriverPipeProtocolException(
                    $"Driver pipe request exceeded the {MaxFrameBytes} byte frame limit.");
            }

            frame.WriteByte(buffer[0]);
        }

        var bytes = frame.ToArray();
        if (bytes.Length > 0 && bytes[^1] == '\r')
        {
            Array.Resize(ref bytes, bytes.Length - 1);
        }

        return Utf8NoBomStrict.GetString(bytes);
    }

    private static bool ValidateAttempt(ProtectedConnectionAttempt attempt)
    {
        if (!IsKnownProtectedService(attempt.ProtectedService))
        {
            return false;
        }

        return ProtectedServiceMap.TryFromPort(attempt.DestinationPort, out var mappedService)
            && mappedService == attempt.ProtectedService;
    }

    private static bool IsKnownProtectedService(ProtectedService service)
    {
        return service is ProtectedService.Rdp or ProtectedService.Ssh or ProtectedService.Telnet;
    }

    private sealed class DriverPipeProtocolException(string message) : Exception(message);
}
