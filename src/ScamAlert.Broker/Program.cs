using ScamAlert.Broker.Configuration;
using ScamAlert.Broker.DriverPipe;
using ScamAlert.Broker.TrayPrompt;
using ScamAlert.Core.Broker;
using ScamAlert.Core.Configuration;
using ScamAlert.Core.Policy;
using ScamAlert.Core.Rules;
using ScamAlert.Core.Signals;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<RemoteAccessPolicyEngine>();
builder.Services.AddSingleton<IProtectionSettingsStore>(_ =>
    new FileProtectionSettingsStore(ScamAlertPaths.SettingsFile));
builder.Services.AddSingleton<IRememberedRuleStore>(_ =>
    new FileRememberedRuleStore(ScamAlertPaths.RulesFile));
builder.Services.AddSingleton<ISignalSink>(_ =>
    new JsonlSignalSink(ScamAlertPaths.SignalFile));
builder.Services.AddSingleton<IConnectionDecisionPrompt, NamedPipeTrayPromptClient>();
builder.Services.AddSingleton<RemoteAccessBroker>();
builder.Services.AddHostedService<NamedPipeDriverServer>();

var host = builder.Build();
host.Run();
