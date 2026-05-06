using ScamAlert.Contracts;
using ScamAlert.Core.Rules;

namespace ScamAlert.Core.Policy;

public sealed class RemoteAccessPolicyEngine
{
    public DriverDecisionResponse? EvaluateRememberedRule(ProtectedConnectionAttempt attempt, RememberedIpRule? rule)
    {
        if (rule is null)
        {
            return null;
        }

        return new DriverDecisionResponse(attempt.EventId, rule.Decision, "rememberedIpRule");
    }

    public DriverDecisionResponse ApplyUserDecision(ProtectedConnectionAttempt attempt, UserDecisionKind decision)
    {
        var driverDecision = decision switch
        {
            UserDecisionKind.AllowOnce => DriverDecisionKind.Allow,
            UserDecisionKind.BlockOnce => DriverDecisionKind.Block,
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unsupported user decision.")
        };

        return new DriverDecisionResponse(attempt.EventId, driverDecision, "userSelected");
    }

    public DriverDecisionResponse ApplyTimeout(ProtectedConnectionAttempt attempt, TimeoutPolicy timeoutPolicy)
    {
        var driverDecision = timeoutPolicy switch
        {
            TimeoutPolicy.AllowOnTimeout => DriverDecisionKind.Allow,
            TimeoutPolicy.BlockOnTimeout => DriverDecisionKind.Block,
            _ => throw new ArgumentOutOfRangeException(nameof(timeoutPolicy), timeoutPolicy, "Unsupported timeout policy.")
        };

        return new DriverDecisionResponse(attempt.EventId, driverDecision, "timeoutPolicy");
    }

    public RememberedIpRule BuildRememberedRule(
        ProtectedConnectionAttempt attempt,
        UserDecisionKind decision,
        DateTimeOffset now)
    {
        var driverDecision = decision switch
        {
            UserDecisionKind.AllowOnce => DriverDecisionKind.Allow,
            UserDecisionKind.BlockOnce => DriverDecisionKind.Block,
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unsupported user decision.")
        };

        return new RememberedIpRule(
            SourceIp: attempt.SourceIp,
            Decision: driverDecision,
            CreatedAt: now,
            UpdatedAt: now);
    }
}
