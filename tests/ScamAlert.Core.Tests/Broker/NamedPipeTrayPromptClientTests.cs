using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ScamAlert.Broker.TrayPrompt;
using ScamAlert.Contracts;

namespace ScamAlert.Core.Tests.Broker;

[Collection("Named pipe tray prompt client")]
public sealed class NamedPipeTrayPromptClientTests
{
    [Fact]
    public async Task RequestDecisionAsync_ReturnsNull_WhenResponseDecisionIsUnknown()
    {
        var observedEventId = Guid.NewGuid();
        var serverTask = RunServerAsync(
            $$"""
              {"observedEventId":"{{observedEventId}}","decision":999,"remember":false}
              """);

        var client = new NamedPipeTrayPromptClient(NullLogger<NamedPipeTrayPromptClient>.Instance);

        var response = await client.RequestDecisionAsync(
            new DecisionPromptRequest(
                observedEventId,
                DateTimeOffset.UtcNow,
                "203.0.113.10",
                3389,
                ProtectedService.Rdp,
                TimeoutPolicy.BlockOnTimeout,
                TimeoutSeconds: 2),
            CancellationToken.None);

        await serverTask;
        Assert.Null(response);
    }

    private static async Task RunServerAsync(string responseLine)
    {
        await using var pipe = new NamedPipeServerStream(
            NamedPipeTrayPromptClient.PipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        await pipe.WaitForConnectionAsync();
        await ReadFrameAsync(pipe);

        await using var writer = new StreamWriter(
            pipe,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            leaveOpen: true)
        {
            AutoFlush = true
        };

        await writer.WriteLineAsync(responseLine);
    }

    private static async Task<string> ReadFrameAsync(Stream stream)
    {
        using var frame = new MemoryStream();
        var buffer = new byte[1];

        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer);
            if (bytesRead == 0 || buffer[0] == '\n')
            {
                break;
            }

            frame.WriteByte(buffer[0]);
        }

        return Encoding.UTF8.GetString(frame.ToArray());
    }
}
