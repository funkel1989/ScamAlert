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
    RemoteAccessPolicyEngine policyEngine,
    IRecentDecisionCache recentDecisions)
{
    public async Task<DriverDecisionResponse> HandleAttemptAsync(
        ProtectedConnectionAttempt attempt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        var settings = await settingsStore.GetAsync(cancellationToken);

        await TryAppendSignalAsync(
            new ObservedInboundAttemptSignal(
                EventId: attempt.EventId,
                OccurredAt: attempt.OccurredAt,
                SourceIp: attempt.SourceIp,
                DestinationPort: attempt.DestinationPort,
                ProtectedService: attempt.ProtectedService,
                LocalPolicyMode: settings.TimeoutPolicy,
                DecisionStatus: DecisionStatus.Pending),
            cancellationToken);

        // Burst dedupe: TCP retransmits within seconds reuse the prior
        // verdict so the user only clicks once per real connection attempt.
        // Tied to (sourceIp, destinationPort); fresh attempts after the TTL
        // expires will re-prompt as expected.
        if (recentDecisions.TryGet(attempt.SourceIp, attempt.DestinationPort, out var cached))
        {
            return new DriverDecisionResponse(attempt.EventId, cached.Decision, cached.Reason);
        }

        var rememberedRule = await rememberedRules.FindBySourceIpAsync(attempt.SourceIp, cancellationToken);
        var rememberedDecision = EvaluateRememberedRule(attempt, rememberedRule);
        if (rememberedDecision is not null)
        {
            CacheDecision(attempt, rememberedDecision);
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
        if (response is null || response.ObservedEventId != attempt.EventId)
        {
            var timeoutDecision = policyEngine.ApplyTimeout(attempt, settings.TimeoutPolicy);
            CacheDecision(attempt, timeoutDecision);
            return timeoutDecision;
        }

        var userDecision = policyEngine.ApplyUserDecision(attempt, response.Decision);
        var now = DateTimeOffset.UtcNow;

        if (response.Remember)
        {
            var rule = policyEngine.BuildRememberedRule(attempt, response.Decision, now);
            await rememberedRules.UpsertAsync(rule, cancellationToken);
        }

        await TryAppendSignalAsync(
            new UserDecisionUpdatedSignal(
                EventId: Guid.NewGuid(),
                ObservedEventId: attempt.EventId,
                OccurredAt: now,
                SourceIp: attempt.SourceIp,
                Decision: response.Decision,
                Remembered: response.Remember,
                Reason: "userSelected"),
            cancellationToken);

        CacheDecision(attempt, userDecision);
        return userDecision;
    }

    private void CacheDecision(ProtectedConnectionAttempt attempt, DriverDecisionResponse decision)
    {
        recentDecisions.Set(
            attempt.SourceIp,
            attempt.DestinationPort,
            new CachedDecision(decision.Decision, decision.Reason));
    }

    private async Task TryAppendSignalAsync<TSignal>(
        TSignal signal,
        CancellationToken cancellationToken)
    {
        try
        {
            await signalSink.AppendAsync(signal, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Signal reporting is best-effort. Local allow/block decisions must continue
            // even when the file sink is locked, unavailable, or out of space.
        }
    }

    private DriverDecisionResponse? EvaluateRememberedRule(
        ProtectedConnectionAttempt attempt,
        RememberedIpRule? rememberedRule)
    {
        try
        {
            return policyEngine.EvaluateRememberedRule(attempt, rememberedRule);
        }
        catch (ArgumentOutOfRangeException) when (rememberedRule is not null)
        {
            return null;
        }
    }
}
