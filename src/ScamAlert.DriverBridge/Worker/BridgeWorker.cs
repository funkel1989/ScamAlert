using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScamAlert.Broker.Client;
using ScamAlert.Contracts;
using ScamAlert.DriverBridge.Configuration;
using ScamAlert.DriverBridge.Driver;

namespace ScamAlert.DriverBridge.Worker;

// Pumps SCAMALERT_CONNECTION_EVENT records out of the kernel queue,
// forwards each one to the broker for an allow/block decision, then
// posts the decision back to the kernel via IOCTL_COMPLETE_EVENT.
public sealed class BridgeWorker(
    IDriverDeviceClient device,
    BrokerDriverPipeClient broker,
    IOptionsMonitor<DriverBridgeOptions> options,
    ILogger<BridgeWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdlePollDelay  = TimeSpan.FromMilliseconds(50);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var current = options.CurrentValue;
        logger.LogInformation(
            "ScamAlert.DriverBridge starting. Device: {DevicePath}; broker pipe: {BrokerPipeName}.",
            current.DevicePath,
            current.BrokerPipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!await TryOpenDeviceAsync(stoppingToken))
            {
                continue;
            }

            await PumpEventsAsync(stoppingToken);
        }

        device.Close();
        logger.LogInformation("ScamAlert.DriverBridge stopping.");
    }

    private async Task<bool> TryOpenDeviceAsync(CancellationToken cancellationToken)
    {
        try
        {
            device.Open();
            logger.LogInformation("Opened driver device.");
            return true;
        }
        catch (Exception ex)
        {
            var reopenBackoff = GetDeviceOpenRetryDelay();
            logger.LogWarning(ex,
                "Driver device unavailable. Retrying in {DelaySeconds}s.",
                reopenBackoff.TotalSeconds);

            try { await Task.Delay(reopenBackoff, cancellationToken); }
            catch (OperationCanceledException) { /* shutdown */ }

            return false;
        }
    }

    private async Task PumpEventsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && device.IsOpen)
        {
            DriverEventPollResult poll;
            try
            {
                poll = device.PollNextEvent();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Driver event poll failed; closing device.");
                device.Close();
                return;
            }

            switch (poll.Outcome)
            {
                case DriverEventPollOutcome.NoEvents:
                    try { await Task.Delay(IdlePollDelay, cancellationToken); }
                    catch (OperationCanceledException) { return; }
                    continue;

                case DriverEventPollOutcome.DeviceUnavailable:
                    logger.LogWarning(
                        "Driver device unavailable mid-pump (Win32 error {Win32Error}). Will reopen.",
                        poll.Win32Error);
                    return;

                case DriverEventPollOutcome.EventReady:
                    await ForwardEventAsync(poll.Event!, cancellationToken);
                    break;
            }
        }
    }

    private async Task ForwardEventAsync(DriverEvent driverEvent, CancellationToken cancellationToken)
    {
        var attempt = new ProtectedConnectionAttempt(
            driverEvent.EventId,
            driverEvent.OccurredAt,
            driverEvent.SourceIp,
            driverEvent.DestinationPort,
            driverEvent.ProtectedService);

        DriverDecisionResponse decision;
        try
        {
            decision = await broker.SendAttemptAsync(attempt, cancellationToken);
        }
        catch (BrokerPipeProtocolException ex)
        {
            logger.LogWarning(ex,
                "Broker did not return a decision for event {EventId}. Defaulting to allow per fail-policy.",
                driverEvent.EventId);
            decision = new DriverDecisionResponse(driverEvent.EventId, DriverDecisionKind.Allow, "brokerUnavailable");
        }

        try
        {
            var native = DriverEventMarshaller.ToNativeDecision(decision.ObservedEventId, decision.Decision);
            device.CompleteEvent(native);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to post decision for event {EventId}.", decision.ObservedEventId);
        }
    }

    private TimeSpan GetDeviceOpenRetryDelay()
    {
        var seconds = options.CurrentValue.DeviceOpenRetryDelaySeconds;
        return seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.FromSeconds(5);
    }
}
