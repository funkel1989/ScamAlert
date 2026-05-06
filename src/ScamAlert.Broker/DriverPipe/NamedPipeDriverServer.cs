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

                var requestLine = await reader.ReadLineAsync(stoppingToken);
                if (string.IsNullOrWhiteSpace(requestLine))
                {
                    continue;
                }

                var attempt = JsonSerializer.Deserialize<ProtectedConnectionAttempt>(
                    requestLine,
                    SignalJson.Options);

                if (attempt is null)
                {
                    continue;
                }

                var decision = await broker.HandleAttemptAsync(attempt, stoppingToken);
                var responseLine = JsonSerializer.Serialize(decision, SignalJson.Options);

                await writer.WriteLineAsync(responseLine.AsMemory(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process driver pipe request.");
            }
        }
    }
}
