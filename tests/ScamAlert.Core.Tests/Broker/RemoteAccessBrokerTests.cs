using ScamAlert.Contracts;
using ScamAlert.Core.Broker;
using ScamAlert.Core.Configuration;
using ScamAlert.Core.Policy;
using ScamAlert.Core.Rules;
using ScamAlert.Core.Signals;

namespace ScamAlert.Core.Tests.Broker;

public sealed class RemoteAccessBrokerTests
{
    [Fact]
    public async Task UnknownAttemptPromptsUserAndWritesObservedThenUserDecisionSignals()
    {
        var fixture = CreateFixture(
            response: new DecisionPromptResponse(CreateAttempt().EventId, UserDecisionKind.AllowOnce, Remember: false));

        var decision = await fixture.Broker.HandleAttemptAsync(CreateAttempt(), CancellationToken.None);

        Assert.Equal(DriverDecisionKind.Allow, decision.Decision);
        Assert.Single(fixture.Prompt.Requests);
        Assert.Collection(
            fixture.SignalSink.Signals,
            signal => Assert.IsType<ObservedInboundAttemptSignal>(signal),
            signal => Assert.IsType<UserDecisionUpdatedSignal>(signal));
    }

    [Fact]
    public async Task RememberedRuleReturnsDecisionWithoutPromptingAndStillWritesObservedSignal()
    {
        var attempt = CreateAttempt();
        var fixture = CreateFixture();
        fixture.RememberedRules.Rule = new RememberedIpRule(
            SourceIp: attempt.SourceIp,
            Decision: DriverDecisionKind.Block,
            CreatedAt: DateTimeOffset.Parse("2026-05-06T12:00:00Z"),
            UpdatedAt: DateTimeOffset.Parse("2026-05-06T12:00:00Z"));

        var decision = await fixture.Broker.HandleAttemptAsync(attempt, CancellationToken.None);

        Assert.Equal(DriverDecisionKind.Block, decision.Decision);
        Assert.Equal("rememberedIpRule", decision.Reason);
        Assert.Empty(fixture.Prompt.Requests);
        Assert.Collection(fixture.SignalSink.Signals, signal => Assert.IsType<ObservedInboundAttemptSignal>(signal));
    }

    [Fact]
    public async Task InvalidRememberedRuleDecisionIsIgnoredAndPromptFlowContinues()
    {
        var attempt = CreateAttempt();
        var fixture = CreateFixture(response: new DecisionPromptResponse(
            attempt.EventId,
            UserDecisionKind.AllowOnce,
            Remember: false));
        fixture.RememberedRules.Rule = new RememberedIpRule(
            SourceIp: attempt.SourceIp,
            Decision: (DriverDecisionKind)999,
            CreatedAt: DateTimeOffset.Parse("2026-05-06T12:00:00Z"),
            UpdatedAt: DateTimeOffset.Parse("2026-05-06T12:00:00Z"));

        var decision = await fixture.Broker.HandleAttemptAsync(attempt, CancellationToken.None);

        Assert.Equal(DriverDecisionKind.Allow, decision.Decision);
        Assert.Single(fixture.Prompt.Requests);
        Assert.Collection(
            fixture.SignalSink.Signals,
            signal => Assert.IsType<ObservedInboundAttemptSignal>(signal),
            signal => Assert.IsType<UserDecisionUpdatedSignal>(signal));
    }

    [Fact]
    public async Task RememberedUserDecisionStoresIpWideRememberedRule()
    {
        var attempt = CreateAttempt();
        var fixture = CreateFixture(response: new DecisionPromptResponse(
            attempt.EventId,
            UserDecisionKind.BlockOnce,
            Remember: true));

        await fixture.Broker.HandleAttemptAsync(attempt, CancellationToken.None);

        var storedRule = Assert.Single(fixture.RememberedRules.Upserts);
        Assert.Equal(attempt.SourceIp, storedRule.SourceIp);
        Assert.Equal(DriverDecisionKind.Block, storedRule.Decision);
    }

    [Fact]
    public async Task NullPromptResponseAppliesConfiguredTimeoutPolicy()
    {
        var fixture = CreateFixture(
            settings: new ProtectionSettings(TimeoutPolicy.AllowOnTimeout, PromptTimeoutSeconds: 11),
            response: null);

        var decision = await fixture.Broker.HandleAttemptAsync(CreateAttempt(), CancellationToken.None);

        Assert.Equal(DriverDecisionKind.Allow, decision.Decision);
        Assert.Equal("timeoutPolicy", decision.Reason);
        Assert.Single(fixture.SignalSink.Signals);
    }

    [Fact]
    public async Task MismatchedPromptResponseObservedEventIdAppliesTimeoutWithoutUserDecisionSignal()
    {
        var fixture = CreateFixture(
            settings: new ProtectionSettings(TimeoutPolicy.BlockOnTimeout, PromptTimeoutSeconds: 10),
            response: new DecisionPromptResponse(
                Guid.Parse("c2a5a82d-f9ff-4af9-a715-943995a74570"),
                UserDecisionKind.AllowOnce,
                Remember: true));

        var decision = await fixture.Broker.HandleAttemptAsync(CreateAttempt(), CancellationToken.None);

        Assert.Equal(DriverDecisionKind.Block, decision.Decision);
        Assert.Equal("timeoutPolicy", decision.Reason);
        Assert.Empty(fixture.RememberedRules.Upserts);
        Assert.Collection(fixture.SignalSink.Signals, signal => Assert.IsType<ObservedInboundAttemptSignal>(signal));
    }

    [Fact]
    public async Task BlockOnTimeoutSettingMapsTimeoutToBlock()
    {
        var fixture = CreateFixture(
            settings: new ProtectionSettings(TimeoutPolicy.BlockOnTimeout, PromptTimeoutSeconds: 11),
            response: null);

        var decision = await fixture.Broker.HandleAttemptAsync(CreateAttempt(), CancellationToken.None);

        Assert.Equal(DriverDecisionKind.Block, decision.Decision);
        Assert.Equal("timeoutPolicy", decision.Reason);
    }

    [Fact]
    public async Task PromptRequestUsesSettingsPromptTimeoutSecondsAndTimeoutPolicy()
    {
        var attempt = CreateAttempt();
        var fixture = CreateFixture(
            settings: new ProtectionSettings(TimeoutPolicy.BlockOnTimeout, PromptTimeoutSeconds: 42),
            response: new DecisionPromptResponse(attempt.EventId, UserDecisionKind.AllowOnce, Remember: false));

        await fixture.Broker.HandleAttemptAsync(attempt, CancellationToken.None);

        var request = Assert.Single(fixture.Prompt.Requests);
        Assert.Equal(attempt.EventId, request.ObservedEventId);
        Assert.Equal(attempt.OccurredAt, request.OccurredAt);
        Assert.Equal(attempt.SourceIp, request.SourceIp);
        Assert.Equal(attempt.DestinationPort, request.DestinationPort);
        Assert.Equal(attempt.ProtectedService, request.ProtectedService);
        Assert.Equal(TimeoutPolicy.BlockOnTimeout, request.LocalPolicyMode);
        Assert.Equal(42, request.TimeoutSeconds);
    }

    [Fact]
    public async Task UserDecisionSignalLinksToObservedEventIdAndUsesUserSelectedReason()
    {
        var attempt = CreateAttempt();
        var fixture = CreateFixture(response: new DecisionPromptResponse(
            attempt.EventId,
            UserDecisionKind.BlockOnce,
            Remember: true));

        await fixture.Broker.HandleAttemptAsync(attempt, CancellationToken.None);

        var signal = Assert.IsType<UserDecisionUpdatedSignal>(fixture.SignalSink.Signals[1]);
        Assert.NotEqual(Guid.Empty, signal.EventId);
        Assert.NotEqual(attempt.EventId, signal.EventId);
        Assert.Equal(attempt.EventId, signal.ObservedEventId);
        Assert.Equal(attempt.SourceIp, signal.SourceIp);
        Assert.Equal(UserDecisionKind.BlockOnce, signal.Decision);
        Assert.True(signal.Remembered);
        Assert.Equal("userSelected", signal.Reason);
    }

    [Fact]
    public async Task InvalidPromptUserDecisionThrowsBeforeUserDecisionSignalOrRememberedRuleUpsert()
    {
        var attempt = CreateAttempt();
        var fixture = CreateFixture(response: new DecisionPromptResponse(
            attempt.EventId,
            (UserDecisionKind)999,
            Remember: false));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => fixture.Broker.HandleAttemptAsync(attempt, CancellationToken.None));

        Assert.Empty(fixture.RememberedRules.Upserts);
        Assert.Collection(fixture.SignalSink.Signals, signal => Assert.IsType<ObservedInboundAttemptSignal>(signal));
    }

    [Fact]
    public async Task ObservedSignalIncludesLocalPolicyModeFromSettings()
    {
        var fixture = CreateFixture(
            settings: new ProtectionSettings(TimeoutPolicy.BlockOnTimeout, PromptTimeoutSeconds: 10),
            response: new DecisionPromptResponse(CreateAttempt().EventId, UserDecisionKind.AllowOnce, Remember: false));

        await fixture.Broker.HandleAttemptAsync(CreateAttempt(), CancellationToken.None);

        var signal = Assert.IsType<ObservedInboundAttemptSignal>(fixture.SignalSink.Signals[0]);
        Assert.Equal(TimeoutPolicy.BlockOnTimeout, signal.LocalPolicyMode);
        Assert.Equal(DecisionStatus.Pending, signal.DecisionStatus);
    }

    [Fact]
    public async Task HandleAttemptAsyncPropagatesCancellationTokenToDependencies()
    {
        var attempt = CreateAttempt();
        using var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        var fixture = CreateFixture(response: new DecisionPromptResponse(
            attempt.EventId,
            UserDecisionKind.BlockOnce,
            Remember: true));

        await fixture.Broker.HandleAttemptAsync(attempt, cancellationToken);

        Assert.Equal(cancellationToken, fixture.SettingsStore.GetCancellationToken);
        Assert.Equal(cancellationToken, fixture.RememberedRules.FindCancellationToken);
        Assert.Equal(cancellationToken, fixture.RememberedRules.UpsertCancellationToken);
        Assert.All(fixture.SignalSink.AppendCancellationTokens, token => Assert.Equal(cancellationToken, token));
        Assert.Equal(cancellationToken, fixture.Prompt.RequestCancellationToken);
    }

    private static BrokerFixture CreateFixture(
        ProtectionSettings? settings = null,
        DecisionPromptResponse? response = null)
    {
        var settingsStore = new TestProtectionSettingsStore(settings ?? ProtectionSettings.Default);
        var rememberedRules = new TestRememberedRuleStore();
        var signalSink = new TestSignalSink();
        var prompt = new TestConnectionDecisionPrompt(response);
        var broker = new RemoteAccessBroker(
            settingsStore,
            rememberedRules,
            signalSink,
            prompt,
            new RemoteAccessPolicyEngine());

        return new BrokerFixture(broker, settingsStore, rememberedRules, signalSink, prompt);
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

    private sealed record BrokerFixture(
        RemoteAccessBroker Broker,
        TestProtectionSettingsStore SettingsStore,
        TestRememberedRuleStore RememberedRules,
        TestSignalSink SignalSink,
        TestConnectionDecisionPrompt Prompt);

    private sealed class TestProtectionSettingsStore(ProtectionSettings settings) : IProtectionSettingsStore
    {
        public CancellationToken GetCancellationToken { get; private set; }

        public Task<ProtectionSettings> GetAsync(CancellationToken cancellationToken)
        {
            GetCancellationToken = cancellationToken;
            return Task.FromResult(settings);
        }

        public Task SaveAsync(ProtectionSettings settings, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TestRememberedRuleStore : IRememberedRuleStore
    {
        public RememberedIpRule? Rule { get; set; }

        public List<RememberedIpRule> Upserts { get; } = [];

        public CancellationToken FindCancellationToken { get; private set; }

        public CancellationToken UpsertCancellationToken { get; private set; }

        public Task<RememberedIpRule?> FindBySourceIpAsync(string sourceIp, CancellationToken cancellationToken)
        {
            FindCancellationToken = cancellationToken;
            return Task.FromResult(Rule?.SourceIp == sourceIp ? Rule : null);
        }

        public Task UpsertAsync(RememberedIpRule rule, CancellationToken cancellationToken)
        {
            UpsertCancellationToken = cancellationToken;
            Upserts.Add(rule);
            Rule = rule;
            return Task.CompletedTask;
        }
    }

    private sealed class TestSignalSink : ISignalSink
    {
        public List<object> Signals { get; } = [];

        public List<CancellationToken> AppendCancellationTokens { get; } = [];

        public Task AppendAsync<TSignal>(TSignal signal, CancellationToken cancellationToken)
        {
            Signals.Add(signal!);
            AppendCancellationTokens.Add(cancellationToken);
            return Task.CompletedTask;
        }
    }

    private sealed class TestConnectionDecisionPrompt(DecisionPromptResponse? response) : IConnectionDecisionPrompt
    {
        public List<DecisionPromptRequest> Requests { get; } = [];

        public CancellationToken RequestCancellationToken { get; private set; }

        public Task<DecisionPromptResponse?> RequestDecisionAsync(
            DecisionPromptRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestCancellationToken = cancellationToken;
            return Task.FromResult(response);
        }
    }
}
