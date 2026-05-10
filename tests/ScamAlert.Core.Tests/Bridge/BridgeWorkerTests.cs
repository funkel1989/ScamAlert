using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ScamAlert.Broker.Client;
using ScamAlert.Contracts;
using ScamAlert.DriverBridge.Configuration;
using ScamAlert.DriverBridge.Driver;
using ScamAlert.DriverBridge.Worker;

namespace ScamAlert.Core.Tests.Bridge;

public sealed class BridgeWorkerTests
{
    [Fact]
    public async Task Worker_pulls_event_from_device_forwards_to_broker_and_completes()
    {
        var attemptId = Guid.NewGuid();
        var fakeDevice = new FakeDriverDeviceClient();
        fakeDevice.QueuedEvents.Enqueue(new DriverEvent(
            attemptId,
            DateTimeOffset.FromUnixTimeMilliseconds(1_770_000_000_000L),
            "203.0.113.10",
            3389,
            ProtectedService.Rdp));

        var pipeName = $"scamalert-test-{Guid.NewGuid():N}";
        using var serverStop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = RunFakeBrokerAsync(pipeName, serverStop.Token,
            (req) => new DriverDecisionResponse(req.EventId, DriverDecisionKind.Block, "userSelected"));

        var broker = new BrokerDriverPipeClient(pipeName, TimeSpan.FromSeconds(2));
        var options = Options.Create(new DriverBridgeOptions
        {
            DevicePath = @"\\.\ScamAlertWfp",
            BrokerPipeName = pipeName,
            BrokerConnectionTimeoutSeconds = 2,
            BrokerRequestTimeoutSeconds = 2,
            DeviceOpenRetryDelaySeconds = 1
        });
        var optionsMonitor = new StaticOptionsMonitor<DriverBridgeOptions>(options.Value);

        var worker = new BridgeWorker(fakeDevice, broker, optionsMonitor, NullLogger<BridgeWorker>.Instance);

        using var workerStop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(workerStop.Token);

        // Spin until the fake device has seen exactly one decision posted.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (fakeDevice.PostedDecisions.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        await worker.StopAsync(CancellationToken.None);
        serverStop.Cancel();
        try { await serverTask; } catch { /* server cancellation is fine */ }

        var decision = Assert.Single(fakeDevice.PostedDecisions);
        Assert.Equal(attemptId, new Guid(decision.EventId));
        Assert.Equal(NativeDriverDecision.Block, decision.Decision);
    }

    [Fact]
    public async Task Worker_falls_back_to_allow_when_broker_pipe_is_unavailable()
    {
        var attemptId = Guid.NewGuid();
        var fakeDevice = new FakeDriverDeviceClient();
        fakeDevice.QueuedEvents.Enqueue(new DriverEvent(
            attemptId,
            DateTimeOffset.UtcNow,
            "198.51.100.7",
            22,
            ProtectedService.Ssh));

        var unreachablePipeName = $"scamalert-unavailable-{Guid.NewGuid():N}";
        var broker = new BrokerDriverPipeClient(
            unreachablePipeName,
            connectionTimeout: TimeSpan.FromMilliseconds(150),
            requestTimeout: TimeSpan.FromSeconds(1));

        var optionsMonitor = new StaticOptionsMonitor<DriverBridgeOptions>(new DriverBridgeOptions
        {
            DevicePath = @"\\.\ScamAlertWfp",
            BrokerPipeName = unreachablePipeName
        });

        var worker = new BridgeWorker(fakeDevice, broker, optionsMonitor, NullLogger<BridgeWorker>.Instance);

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(stop.Token);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (fakeDevice.PostedDecisions.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        await worker.StopAsync(CancellationToken.None);

        var decision = Assert.Single(fakeDevice.PostedDecisions);
        Assert.Equal(attemptId, new Guid(decision.EventId));
        Assert.Equal(NativeDriverDecision.Allow, decision.Decision);
    }

    private static async Task RunFakeBrokerAsync(
        string pipeName,
        CancellationToken cancellationToken,
        Func<ProtectedConnectionAttempt, DriverDecisionResponse> handler)
    {
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        await server.WaitForConnectionAsync(cancellationToken);

        using var reader = new StreamReader(server, Encoding.UTF8, false, leaveOpen: true);
        await using var writer = new StreamWriter(
            server,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            leaveOpen: true)
        {
            AutoFlush = true
        };

        var requestLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(requestLine)) return;

        var attempt = JsonSerializer.Deserialize<ProtectedConnectionAttempt>(requestLine, SignalJson.Options);
        if (attempt is null) return;

        var response = handler(attempt);
        await writer.WriteLineAsync(JsonSerializer.Serialize(response, SignalJson.Options).AsMemory(), cancellationToken);
    }

    private sealed class FakeDriverDeviceClient : IDriverDeviceClient
    {
        public Queue<DriverEvent> QueuedEvents { get; } = new();
        public List<NativeConnectionDecision> PostedDecisions { get; } = new();
        public bool IsOpen { get; private set; }

        public void Open() => IsOpen = true;

        public void Close() => IsOpen = false;

        public DriverEventPollResult PollNextEvent()
        {
            if (!IsOpen) return new(DriverEventPollOutcome.DeviceUnavailable, null, null);
            if (QueuedEvents.Count == 0) return new(DriverEventPollOutcome.NoEvents, null, null);
            return new(DriverEventPollOutcome.EventReady, QueuedEvents.Dequeue(), null);
        }

        public void CompleteEvent(NativeConnectionDecision decision) => PostedDecisions.Add(decision);

        public void Dispose() => Close();
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
