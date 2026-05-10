using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ScamAlert.Broker.Client;
using ScamAlert.DriverBridge.Configuration;
using ScamAlert.DriverBridge.Driver;
using ScamAlert.DriverBridge.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<DriverBridgeOptions>(
    builder.Configuration.GetSection(DriverBridgeOptions.SectionName));

builder.Services.AddSingleton<BrokerDriverPipeClient>(sp =>
{
    var o = sp.GetRequiredService<IOptionsMonitor<DriverBridgeOptions>>().CurrentValue;
    return new BrokerDriverPipeClient(
        o.BrokerPipeName,
        TimeSpan.FromSeconds(o.BrokerConnectionTimeoutSeconds),
        TimeSpan.FromSeconds(o.BrokerRequestTimeoutSeconds));
});

builder.Services.AddSingleton<IDriverDeviceClient>(sp =>
{
    var o = sp.GetRequiredService<IOptionsMonitor<DriverBridgeOptions>>().CurrentValue;
    return new DriverDeviceClient(o.DevicePath);
});

builder.Services.AddHostedService<BridgeWorker>();

var host = builder.Build();
host.Run();
