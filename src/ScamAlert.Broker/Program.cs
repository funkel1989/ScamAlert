using Microsoft.Extensions.Options;
using ScamAlert.Broker.CloudAlerts;
using ScamAlert.Broker.Configuration;
using ScamAlert.Broker.DriverPipe;
using ScamAlert.Broker.TrayPrompt;
using ScamAlert.Core.Broker;
using ScamAlert.Core.CloudAlerts;
using ScamAlert.Core.Configuration;
using ScamAlert.Core.Policy;
using ScamAlert.Core.Rules;
using ScamAlert.Core.Signals;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<CloudAlertOptions>(builder.Configuration.GetSection(CloudAlertOptions.SectionName));
builder.Services.AddSingleton<ICloudAlertEnqueueSource, CloudAlertEnqueueSource>();
builder.Services.AddSingleton<FileBackedAlertDeduper>(sp => new FileBackedAlertDeduper(
    ScamAlertPaths.CloudAlertDedupeStateFile,
    sp.GetRequiredService<ICloudAlertEnqueueSource>(),
    sp.GetRequiredService<ILogger<FileBackedAlertDeduper>>()));
builder.Services.AddSingleton<CloudOutboxStore>(sp => new CloudOutboxStore(
    ScamAlertPaths.CloudAlertPendingOutboxFile,
    ScamAlertPaths.CloudAlertDeadLetterFile,
    sp.GetRequiredService<ILogger<CloudOutboxStore>>()));
builder.Services.AddSingleton<ObservedInboundCloudEnqueueSignalSink>();
builder.Services.AddSingleton<ISignalSink>(sp =>
{
    var jsonl = new JsonlSignalSink(ScamAlertPaths.SignalFile);
    var cloud = sp.GetRequiredService<ObservedInboundCloudEnqueueSignalSink>();
    return new CompositeSignalSink(
        new ISignalSink[] { jsonl, cloud },
        sp.GetRequiredService<ILogger<CompositeSignalSink>>());
});

builder.Services.AddHttpClient("CloudAlerts", (sp, client) =>
{
    var o = sp.GetRequiredService<IOptionsMonitor<CloudAlertOptions>>().CurrentValue;
    if (!string.IsNullOrWhiteSpace(o.BaseUrl))
    {
        var trimmed = o.BaseUrl.TrimEnd('/') + "/";
        client.BaseAddress = new Uri(trimmed);
    }

    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddSingleton<RemoteAccessPolicyEngine>();
builder.Services.AddSingleton<IProtectionSettingsStore>(_ =>
    new FileProtectionSettingsStore(ScamAlertPaths.SettingsFile));
builder.Services.AddSingleton<IRememberedRuleStore>(_ =>
    new FileRememberedRuleStore(ScamAlertPaths.RulesFile));
builder.Services.AddSingleton<IConnectionDecisionPrompt, NamedPipeTrayPromptClient>();
builder.Services.AddSingleton<RemoteAccessBroker>();
builder.Services.AddHostedService<NamedPipeDriverServer>();
builder.Services.AddHostedService<CloudAlertDeliveryWorker>();

var host = builder.Build();
host.Run();
