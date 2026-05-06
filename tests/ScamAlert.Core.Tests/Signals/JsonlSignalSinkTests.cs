using ScamAlert.Contracts;
using ScamAlert.Core.Signals;

namespace ScamAlert.Core.Tests.Signals;

public sealed class JsonlSignalSinkTests
{
    [Fact]
    public async Task AppendAsyncWritesOneJsonObjectPerLine()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ScamAlert.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "signals.jsonl");
        var sink = new JsonlSignalSink(path);

        await sink.AppendAsync(
            new ObservedInboundAttemptSignal(
                EventId: Guid.Parse("9a669180-8546-4ed7-aea8-45df4e716d87"),
                OccurredAt: DateTimeOffset.Parse("2026-05-06T12:00:00Z"),
                SourceIp: "203.0.113.7",
                DestinationPort: 3389,
                ProtectedService: ProtectedService.Rdp,
                LocalPolicyMode: TimeoutPolicy.AllowOnTimeout,
                DecisionStatus: DecisionStatus.Pending),
            CancellationToken.None);

        await sink.AppendAsync(
            new UserDecisionUpdatedSignal(
                EventId: Guid.Parse("cfb6bfc1-a88d-45e8-9e20-627e15327d21"),
                ObservedEventId: Guid.Parse("9a669180-8546-4ed7-aea8-45df4e716d87"),
                OccurredAt: DateTimeOffset.Parse("2026-05-06T12:00:03Z"),
                SourceIp: "203.0.113.7",
                Decision: UserDecisionKind.AllowOnce,
                Remembered: false,
                Reason: "user selected allow"),
            CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(path, CancellationToken.None);

        Assert.Equal(2, lines.Length);
        Assert.Contains("\"eventType\":\"ObservedInboundAttempt\"", lines[0]);
        Assert.Contains("\"eventType\":\"UserDecisionUpdated\"", lines[1]);
    }
}
