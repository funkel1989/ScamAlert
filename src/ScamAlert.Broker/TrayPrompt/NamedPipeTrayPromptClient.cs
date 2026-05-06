using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ScamAlert.Contracts;
using ScamAlert.Core.Broker;

namespace ScamAlert.Broker.TrayPrompt;

public sealed class NamedPipeTrayPromptClient : IConnectionDecisionPrompt
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
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            var connectTimeout = TimeSpan.FromMilliseconds(
                Math.Min(MaxConnectTimeout.TotalMilliseconds, TimeSpan.FromSeconds(request.TimeoutSeconds).TotalMilliseconds));

            await pipe.ConnectAsync((int)connectTimeout.TotalMilliseconds, timeout.Token);

            await using var writer = new StreamWriter(
                pipe,
                Utf8NoBomStrict,
                leaveOpen: true)
            {
                AutoFlush = true
            };

            var requestLine = JsonSerializer.Serialize(request, SignalJson.Options);
            await writer.WriteLineAsync(requestLine.AsMemory(), timeout.Token);

            var responseLine = await ReadFrameAsync(pipe, timeout.Token);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                return null;
            }

            var response = JsonSerializer.Deserialize<DecisionPromptResponse>(
                responseLine,
                SignalJson.Options);

            if (response is null || response.ObservedEventId != request.ObservedEventId)
            {
                return null;
            }

            return response;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
        catch (TrayPromptProtocolException)
        {
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

    private sealed class TrayPromptProtocolException(string message) : Exception(message);
}
