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
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);

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

                var requestLine = await reader.ReadLineAsync(connectionToken);
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
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process driver pipe request.");
            }
        }
    }
}
