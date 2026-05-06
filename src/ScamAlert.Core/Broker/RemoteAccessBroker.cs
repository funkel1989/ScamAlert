using ScamAlert.Contracts;
using ScamAlert.Core.Configuration;
using ScamAlert.Core.Policy;
using ScamAlert.Core.Rules;
using ScamAlert.Core.Signals;

namespace ScamAlert.Core.Broker;

public sealed class RemoteAccessBroker(
    IProtectionSettingsStore settingsStore,
    IRememberedRuleStore rememberedRules,
    ISignalSink signalSink,
    IConnectionDecisionPrompt prompt,
    RemoteAccessPolicyEngine policyEngine)
{
    public async Task<DriverDecisionResponse> HandleAttemptAsync(
        ProtectedConnectionAttempt attempt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        var settings = await settingsStore.GetAsync(cancellationToken);

        await signalSink.AppendAsync(
            new ObservedInboundAttemptSignal(
                EventId: attempt.EventId,
                OccurredAt: attempt.OccurredAt,
                SourceIp: attempt.SourceIp,
                DestinationPort: attempt.DestinationPort,
                ProtectedService: attempt.ProtectedService,
                LocalPolicyMode: settings.TimeoutPolicy,
                DecisionStatus: DecisionStatus.Pending),
            cancellationToken);

        var rememberedRule = await rememberedRules.FindBySourceIpAsync(attempt.SourceIp, cancellationToken);
        var rememberedDecision = policyEngine.EvaluateRememberedRule(attempt, rememberedRule);
        if (rememberedDecision is not null)
        {
            return rememberedDecision;
        }

        var request = new DecisionPromptRequest(
            ObservedEventId: attempt.EventId,
            OccurredAt: attempt.OccurredAt,
            SourceIp: attempt.SourceIp,
            DestinationPort: attempt.DestinationPort,
            ProtectedService: attempt.ProtectedService,
            LocalPolicyMode: settings.TimeoutPolicy,
            TimeoutSeconds: settings.PromptTimeoutSeconds);

        var response = await prompt.RequestDecisionAsync(request, cancellationToken);
        if (response is null)
        {
            return policyEngine.ApplyTimeout(attempt, settings.TimeoutPolicy);
        }

        if (response.Remember)
        {
            var rule = policyEngine.BuildRememberedRule(attempt, response.Decision, DateTimeOffset.UtcNow);
            await rememberedRules.UpsertAsync(rule, cancellationToken);
        }

        await signalSink.AppendAsync(
            new UserDecisionUpdatedSignal(
                EventId: Guid.NewGuid(),
                ObservedEventId: attempt.EventId,
                OccurredAt: DateTimeOffset.UtcNow,
                SourceIp: attempt.SourceIp,
                Decision: response.Decision,
                Remembered: response.Remember,
                Reason: "userSelected"),
            cancellationToken);

        return policyEngine.ApplyUserDecision(attempt, response.Decision);
    }
}
