using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ScamAlert.Contracts;
using ScamAlert.Core.Broker;

namespace ScamAlert.Broker.TrayPrompt;

public sealed class NamedPipeTrayPromptClient(ILogger<NamedPipeTrayPromptClient> logger)
    : IConnectionDecisionPrompt
{
    public const string PipeName = "scamalert-tray-prompts";

    private const int MaxFrameBytes = 16 * 1024;
    private static readonly TimeSpan MaxConnectTimeout = TimeSpan.FromSeconds(1);
    private static readonly UTF8Encoding Utf8NoBomStrict = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public async Task<DecisionPromptResponse?> RequestDecisionAsync(
        DecisionPromptRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TimeoutSeconds <= 0)
        {
            logger.LogWarning("Tray prompt skipped: request.TimeoutSeconds={TimeoutSeconds}.", request.TimeoutSeconds);
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));

        var connectTimeout = TimeSpan.FromMilliseconds(
            Math.Min(MaxConnectTimeout.TotalMilliseconds, TimeSpan.FromSeconds(request.TimeoutSeconds).TotalMilliseconds));

        logger.LogInformation(
            "Tray prompt START. EventId={EventId} Service={Service} Port={Port} TimeoutSeconds={TimeoutSeconds} ConnectTimeoutMs={ConnectTimeoutMs}",
            request.ObservedEventId, request.ProtectedService, request.DestinationPort,
            request.TimeoutSeconds, (int)connectTimeout.TotalMilliseconds);

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipe.ConnectAsync((int)connectTimeout.TotalMilliseconds, timeout.Token);
            logger.LogInformation("Tray prompt CONNECTED. EventId={EventId}", request.ObservedEventId);

            await using var writer = new StreamWriter(
                pipe,
                Utf8NoBomStrict,
                leaveOpen: true)
            {
                AutoFlush = true
            };

            var requestLine = JsonSerializer.Serialize(request, SignalJson.Options);
            await writer.WriteLineAsync(requestLine.AsMemory(), timeout.Token);
            var requestBytes = Utf8NoBomStrict.GetByteCount(requestLine);
            logger.LogInformation(
                "Tray prompt SENT. EventId={EventId} RequestBytes={RequestBytes}",
                request.ObservedEventId, requestBytes);

            var responseLine = await ReadFrameAsync(pipe, timeout.Token);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                logger.LogWarning(
                    "Tray prompt EMPTY response. EventId={EventId}", request.ObservedEventId);
                return null;
            }

            logger.LogInformation(
                "Tray prompt RECEIVED. EventId={EventId} ResponseBytes={ResponseBytes}",
                request.ObservedEventId, Utf8NoBomStrict.GetByteCount(responseLine));

            var response = JsonSerializer.Deserialize<DecisionPromptResponse>(
                responseLine,
                SignalJson.Options);

            if (response is null)
            {
                logger.LogWarning("Tray prompt response failed to deserialize. EventId={EventId}", request.ObservedEventId);
                return null;
            }

            if (response.ObservedEventId != request.ObservedEventId)
            {
                logger.LogWarning(
                    "Tray prompt response EventId mismatch. Sent={SentId} Got={GotId}",
                    request.ObservedEventId, response.ObservedEventId);
                return null;
            }

            if (!IsKnownUserDecision(response.Decision))
            {
                logger.LogWarning(
                    "Tray prompt response decision not recognized. EventId={EventId} Decision={Decision}",
                    request.ObservedEventId, response.Decision);
                return null;
            }

            logger.LogInformation(
                "Tray prompt DONE. EventId={EventId} Decision={Decision} Remember={Remember}",
                request.ObservedEventId, response.Decision, response.Remember);
            return response;
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(
                "Tray prompt CANCELED. EventId={EventId} CallerCanceled={CallerCanceled} TimeoutFired={TimeoutFired} Reason={Reason}",
                request.ObservedEventId, cancellationToken.IsCancellationRequested, timeout.IsCancellationRequested, ex.Message);
            return null;
        }
        catch (TimeoutException ex)
        {
            logger.LogWarning(
                "Tray prompt TIMEOUT (connect). EventId={EventId} Reason={Reason}",
                request.ObservedEventId, ex.Message);
            return null;
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Tray prompt IOException. EventId={EventId}", request.ObservedEventId);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Tray prompt UnauthorizedAccessException. EventId={EventId}", request.ObservedEventId);
            return null;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Tray prompt JsonException. EventId={EventId}", request.ObservedEventId);
            return null;
        }
        catch (DecoderFallbackException ex)
        {
            logger.LogWarning(ex, "Tray prompt DecoderFallbackException. EventId={EventId}", request.ObservedEventId);
            return null;
        }
        catch (TrayPromptProtocolException ex)
        {
            logger.LogWarning(ex, "Tray prompt protocol error. EventId={EventId}", request.ObservedEventId);
            return null;
        }
    }

    private static async Task<string> ReadFrameAsync(
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

                throw new TrayPromptProtocolException("Tray prompt response ended before LF.");
            }

            if (buffer[0] == '\n')
            {
                break;
            }

            if (frame.Length >= MaxFrameBytes)
            {
                throw new TrayPromptProtocolException(
                    $"Tray prompt response exceeded the {MaxFrameBytes} byte frame limit.");
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

    private static bool IsKnownUserDecision(UserDecisionKind decision)
    {
        return decision is UserDecisionKind.AllowOnce or UserDecisionKind.BlockOnce;
    }

    private sealed class TrayPromptProtocolException(string message) : Exception(message);
}
