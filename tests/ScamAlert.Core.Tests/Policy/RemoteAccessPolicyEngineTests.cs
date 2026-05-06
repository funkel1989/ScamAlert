using ScamAlert.Contracts;
using ScamAlert.Core.Policy;
using ScamAlert.Core.Rules;

namespace ScamAlert.Core.Tests.Policy;

public sealed class RemoteAccessPolicyEngineTests
{
    [Theory]
    [InlineData(DriverDecisionKind.Allow)]
    [InlineData(DriverDecisionKind.Block)]
    public void EvaluateRememberedRuleReturnsStoredDecision(DriverDecisionKind decision)
    {
        var attempt = CreateAttempt();
        var rule = new RememberedIpRule(
            SourceIp: attempt.SourceIp,
            Decision: decision,
            CreatedAt: DateTimeOffset.Parse("2026-05-06T12:00:00Z"),
            UpdatedAt: DateTimeOffset.Parse("2026-05-06T12:01:00Z"));
        var engine = new RemoteAccessPolicyEngine();

        var response = engine.EvaluateRememberedRule(attempt, rule);

        Assert.NotNull(response);
        Assert.Equal(attempt.EventId, response.ObservedEventId);
        Assert.Equal(decision, response.Decision);
        Assert.Equal("rememberedIpRule", response.Reason);
    }

    [Fact]
    public void EvaluateRememberedRuleReturnsNullWhenNoRuleExists()
    {
        var engine = new RemoteAccessPolicyEngine();

        var response = engine.EvaluateRememberedRule(CreateAttempt(), null);

        Assert.Null(response);
    }

    [Theory]
    [InlineData(TimeoutPolicy.AllowOnTimeout, DriverDecisionKind.Allow)]
    [InlineData(TimeoutPolicy.BlockOnTimeout, DriverDecisionKind.Block)]
    public void ApplyTimeoutMapsPolicyToDecision(TimeoutPolicy timeoutPolicy, DriverDecisionKind expectedDecision)
    {
        var attempt = CreateAttempt();
        var engine = new RemoteAccessPolicyEngine();

        var response = engine.ApplyTimeout(attempt, timeoutPolicy);

        Assert.Equal(attempt.EventId, response.ObservedEventId);
        Assert.Equal(expectedDecision, response.Decision);
        Assert.Equal("timeoutPolicy", response.Reason);
    }

    [Fact]
    public void ApplyTimeoutThrowsForUnsupportedPolicy()
    {
        var engine = new RemoteAccessPolicyEngine();

        Assert.Throws<ArgumentOutOfRangeException>(() => engine.ApplyTimeout(CreateAttempt(), (TimeoutPolicy)99));
    }

    [Theory]
    [InlineData(UserDecisionKind.AllowOnce, DriverDecisionKind.Allow)]
    [InlineData(UserDecisionKind.BlockOnce, DriverDecisionKind.Block)]
    public void ApplyUserDecisionMapsUserChoiceToDecision(UserDecisionKind userDecision, DriverDecisionKind expectedDecision)
    {
        var attempt = CreateAttempt();
        var engine = new RemoteAccessPolicyEngine();

        var response = engine.ApplyUserDecision(attempt, userDecision);

        Assert.Equal(attempt.EventId, response.ObservedEventId);
        Assert.Equal(expectedDecision, response.Decision);
        Assert.Equal("userSelected", response.Reason);
    }

    [Fact]
    public void ApplyUserDecisionThrowsForUnsupportedDecision()
    {
        var engine = new RemoteAccessPolicyEngine();

        Assert.Throws<ArgumentOutOfRangeException>(() => engine.ApplyUserDecision(CreateAttempt(), (UserDecisionKind)99));
    }

    [Theory]
    [InlineData(UserDecisionKind.AllowOnce, DriverDecisionKind.Allow)]
    [InlineData(UserDecisionKind.BlockOnce, DriverDecisionKind.Block)]
    public void BuildRememberedRuleUsesAttemptSourceIpAndMapsUserChoice(UserDecisionKind userDecision, DriverDecisionKind expectedDecision)
    {
        var attempt = CreateAttempt();
        var now = DateTimeOffset.Parse("2026-05-06T13:00:00Z");
        var engine = new RemoteAccessPolicyEngine();

        var rule = engine.BuildRememberedRule(attempt, userDecision, now);

        Assert.Equal(attempt.SourceIp, rule.SourceIp);
        Assert.Equal(expectedDecision, rule.Decision);
        Assert.Equal(now, rule.CreatedAt);
        Assert.Equal(now, rule.UpdatedAt);
    }

    [Fact]
    public void BuildRememberedRuleThrowsForUnsupportedDecision()
    {
        var engine = new RemoteAccessPolicyEngine();

        Assert.Throws<ArgumentOutOfRangeException>(() => engine.BuildRememberedRule(
            CreateAttempt(),
            (UserDecisionKind)99,
            DateTimeOffset.Parse("2026-05-06T13:00:00Z")));
    }

    private static ProtectedConnectionAttempt CreateAttempt()
    {
        return new ProtectedConnectionAttempt(
            EventId: Guid.Parse("55db6e3a-f6ac-40f5-b46e-fd7951704670"),
            OccurredAt: DateTimeOffset.Parse("2026-05-06T12:34:56Z"),
            SourceIp: "203.0.113.7",
            DestinationPort: 3389,
            ProtectedService: ProtectedService.Rdp);
    }
}
