using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ScamAlert.Broker.Client;
using ScamAlert.DriverBridge.Configuration;
using ScamAlert.DriverBridge.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<DriverBridgeOptions>(
    builder.Configuration.GetSection(DriverBridgeOptions.SectionName));

builder.Services.AddSingleton<BrokerDriverPipeClient>(sp =>
{
    var monitor = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<DriverBridgeOptions>>();
    var o = monitor.CurrentValue;
    return new BrokerDriverPipeClient(
        o.BrokerPipeName,
        TimeSpan.FromSeconds(o.BrokerConnectionTimeoutSeconds),
        TimeSpan.FromSeconds(o.BrokerRequestTimeoutSeconds));
});

builder.Services.AddHostedService<BridgeWorker>();

var host = builder.Build();
host.Run();
