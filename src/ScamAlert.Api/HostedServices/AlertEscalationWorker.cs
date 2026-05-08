using Microsoft.Extensions.Options;
using ScamAlert.Api.Services.Alerts;

namespace ScamAlert.Api.HostedServices;

public sealed class AlertEscalationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<AlertsOptions> options,
    ILogger<AlertEscalationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = TimeSpan.FromSeconds(Math.Max(1, options.Value.EscalationPollIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<AlertEscalationProcessor>();
                await processor.ProcessDueAlertsAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Alert escalation pass failed.");
            }

            try
            {
                await Task.Delay(poll, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
