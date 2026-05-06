using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ScamAlert.Contracts;

namespace ScamAlert.Tray;

public sealed class PromptPipeServer : IDisposable
{
    public const string PipeName = "scamalert-tray-prompts";

    private const int MaxFrameBytes = 16 * 1024;
    private readonly CancellationTokenSource stop = new();
    private readonly SynchronizationContext uiContext;
    private bool started;
    private static readonly UTF8Encoding Utf8NoBomStrict = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public PromptPipeServer(SynchronizationContext uiContext)
    {
        this.uiContext = uiContext;
    }

    public void Start()
    {
        if (started)
        {
            return;
        }

        started = true;
        _ = Task.Run(RunAsync);
    }

    public void Dispose()
    {
        stop.Cancel();
        stop.Dispose();
    }

    private async Task RunAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(stop.Token);

                await using var writer = new StreamWriter(
                    pipe,
                    Utf8NoBomStrict,
                    leaveOpen: true)
                {
                    AutoFlush = true
                };

                var requestLine = await ReadFrameAsync(pipe, stop.Token);
                if (string.IsNullOrWhiteSpace(requestLine))
                {
                    continue;
                }

                var request = JsonSerializer.Deserialize<DecisionPromptRequest>(
                    requestLine,
                    SignalJson.Options);

                if (request is null || !ValidateRequest(request))
                {
                    continue;
                }

                var response = await ShowPromptAsync(request, stop.Token);
                var responseLine = JsonSerializer.Serialize(response, SignalJson.Options);
                await writer.WriteLineAsync(responseLine.AsMemory(), stop.Token);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (JsonException)
            {
            }
            catch (DecoderFallbackException)
            {
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (PromptPipeProtocolException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (Exception)
            {
            }
        }
    }

    private Task<DecisionPromptResponse?> ShowPromptAsync(
        DecisionPromptRequest request,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<DecisionPromptResponse?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        uiContext.Post(_ =>
        {
            try
            {
                using var form = new ConnectionPromptForm(request);
                form.ShowDialog();
                completion.TrySetResult(form.PromptResponse);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }, null);

        return completion.Task.WaitAsync(cancellationToken);
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

                throw new PromptPipeProtocolException("Tray prompt request ended before LF.");
            }

            if (buffer[0] == '\n')
            {
                break;
            }

            if (frame.Length >= MaxFrameBytes)
            {
                throw new PromptPipeProtocolException(
                    $"Tray prompt request exceeded the {MaxFrameBytes} byte frame limit.");
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

    private static bool ValidateRequest(DecisionPromptRequest request)
    {
        if (!IsKnownProtectedService(request.ProtectedService))
        {
            return false;
        }

        return ProtectedServiceMap.TryFromPort(request.DestinationPort, out var mappedService)
            && mappedService == request.ProtectedService;
    }

    private static bool IsKnownProtectedService(ProtectedService service)
    {
        return service is ProtectedService.Rdp or ProtectedService.Ssh or ProtectedService.Telnet;
    }

    private sealed class PromptPipeProtocolException(string message) : Exception(message);
}
