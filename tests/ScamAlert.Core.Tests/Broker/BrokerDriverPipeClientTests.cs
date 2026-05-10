using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ScamAlert.Broker.Client;
using ScamAlert.Contracts;

namespace ScamAlert.Core.Tests.Broker;

public sealed class BrokerDriverPipeClientTests
{
    [Fact]
    public async Task SendAttemptAsync_returns_decision_when_event_id_matches()
    {
        var pipeName = $"scamalert-test-{Guid.NewGuid():N}";
        var attempt = CreateAttempt();
        var serverTask = RunServerAsync(
            pipeName,
            (req) => new DriverDecisionResponse(req.EventId, DriverDecisionKind.Allow, "userSelected"));

        var client = new BrokerDriverPipeClient(pipeName, TimeSpan.FromSeconds(2));
        var decision = await client.SendAttemptAsync(attempt);

        Assert.Equal(attempt.EventId, decision.ObservedEventId);
        Assert.Equal(DriverDecisionKind.Allow, decision.Decision);
        Assert.Equal("userSelected", decision.Reason);

        await serverTask;
    }

    [Fact]
    public async Task SendAttemptAsync_throws_when_event_id_mismatches()
    {
        var pipeName = $"scamalert-test-{Guid.NewGuid():N}";
        var attempt = CreateAttempt();
        var serverTask = RunServerAsync(
            pipeName,
            (_) => new DriverDecisionResponse(Guid.NewGuid(), DriverDecisionKind.Allow, "userSelected"));

        var client = new BrokerDriverPipeClient(pipeName, TimeSpan.FromSeconds(2));

        await Assert.ThrowsAsync<BrokerPipeProtocolException>(() => client.SendAttemptAsync(attempt));
        await serverTask;
    }

    [Fact]
    public async Task SendAttemptAsync_throws_when_response_is_malformed_json()
    {
        var pipeName = $"scamalert-test-{Guid.NewGuid():N}";
        var attempt = CreateAttempt();
        var serverTask = RunServerRawResponseAsync(pipeName, "not-valid-json\n");

        var client = new BrokerDriverPipeClient(pipeName, TimeSpan.FromSeconds(2));

        await Assert.ThrowsAsync<BrokerPipeProtocolException>(() => client.SendAttemptAsync(attempt));
        await serverTask;
    }

    [Fact]
    public async Task SendAttemptAsync_throws_when_pipe_is_unavailable()
    {
        var pipeName = $"scamalert-test-unavailable-{Guid.NewGuid():N}";
        var client = new BrokerDriverPipeClient(
            pipeName,
            connectionTimeout: TimeSpan.FromMilliseconds(200),
            requestTimeout: TimeSpan.FromSeconds(2));

        await Assert.ThrowsAsync<BrokerPipeProtocolException>(
            () => client.SendAttemptAsync(CreateAttempt()));
    }

    [Fact]
    public void Constructor_rejects_connection_timeout_that_cannot_be_represented_by_named_pipe_client()
    {
        var pipeName = $"scamalert-test-timeout-{Guid.NewGuid():N}";

        Assert.Throws<ArgumentOutOfRangeException>(() => new BrokerDriverPipeClient(
            pipeName,
            connectionTimeout: TimeSpan.FromMilliseconds((double)int.MaxValue + 1),
            requestTimeout: TimeSpan.FromSeconds(2)));
    }

    private static ProtectedConnectionAttempt CreateAttempt() => new(
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        "203.0.113.10",
        3389,
        ProtectedService.Rdp);

    private static async Task RunServerAsync(
        string pipeName,
        Func<ProtectedConnectionAttempt, DriverDecisionResponse> handler)
    {
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        await server.WaitForConnectionAsync();

        using var reader = new StreamReader(server, Encoding.UTF8, false, leaveOpen: true);
        await using var writer = new StreamWriter(
            server,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            leaveOpen: true)
        {
            AutoFlush = true
        };

        var requestLine = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(requestLine)) return;

        var attempt = JsonSerializer.Deserialize<ProtectedConnectionAttempt>(requestLine, SignalJson.Options);
        if (attempt is null) return;

        var response = handler(attempt);
        await writer.WriteLineAsync(JsonSerializer.Serialize(response, SignalJson.Options));
    }

    private static async Task RunServerRawResponseAsync(string pipeName, string rawResponseFrame)
    {
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        await server.WaitForConnectionAsync();

        using var reader = new StreamReader(server, Encoding.UTF8, false, leaveOpen: true);
        await using var writer = new StreamWriter(
            server,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            leaveOpen: true)
        {
            AutoFlush = true
        };

        await reader.ReadLineAsync();
        await writer.WriteAsync(rawResponseFrame);
    }
}
