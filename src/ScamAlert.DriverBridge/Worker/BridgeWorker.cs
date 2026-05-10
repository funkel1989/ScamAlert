using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScamAlert.DriverBridge.Configuration;

namespace ScamAlert.DriverBridge.Worker;

// Task 5 will replace ExecuteAsync with the real
// open-device / GET_EVENT / forward-to-broker / COMPLETE_EVENT loop.
// For now this shell exists so the host process runs and is wired
// for DI, logging, and configuration.
public sealed class BridgeWorker(
    IOptionsMonitor<DriverBridgeOptions> options,
    ILogger<BridgeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var current = options.CurrentValue;
        logger.LogInformation(
            "ScamAlert.DriverBridge starting. Device: {DevicePath}; broker pipe: {BrokerPipeName}.",
            current.DevicePath,
            current.BrokerPipeName);

        try
        {
            // Idle until shutdown. The real pump arrives in Task 5.
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown.
        }

        logger.LogInformation("ScamAlert.DriverBridge stopping.");
    }
}
