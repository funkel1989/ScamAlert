using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ScamAlert.Broker.Configuration;
using ScamAlert.Contracts;
using ScamAlert.Core.CloudAlerts;

namespace ScamAlert.Broker.CloudAlerts;

public sealed class CloudAlertDeliveryWorker(
    IHttpClientFactory httpClientFactory,
    CloudOutboxStore outboxStore,
    IOptionsMonitor<CloudAlertOptions> optionsMonitor,
    ILogger<CloudAlertDeliveryWorker> logger) : BackgroundService
{
    private static readonly Random JitterRandom = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = optionsMonitor.CurrentValue;
            var poll = TimeSpan.FromSeconds(Math.Max(1, options.PollIntervalSeconds));

            if (!options.Enabled || string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                try
                {
                    await Task.Delay(poll, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                continue;
            }

            try
            {
                await ProcessOutboxOnceAsync(options, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Cloud alert delivery pass failed.");
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

    private async Task ProcessOutboxOnceAsync(CloudAlertOptions options, CancellationToken cancellationToken)
    {
        var pending = await outboxStore.ReadPendingAsync(cancellationToken).ConfigureAwait(false);
        if (pending.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var remaining = new List<CloudAlertOutboxItem>();
        var client = httpClientFactory.CreateClient("CloudAlerts");

        foreach (var item in pending)
        {
            if (item.NextAttemptUtc > now)
            {
                remaining.Add(item);
                continue;
            }

            if (item.AttemptCount >= options.MaxDeliveryAttempts)
            {
                var serialized = JsonSerializer.Serialize(item, SignalJson.Options);
                await outboxStore.AppendDeadLetterAsync(serialized, "maxAttempts", cancellationToken)
                    .ConfigureAwait(false);
                logger.LogWarning(
                    "Cloud alert {OutboxId} moved to dead-letter after {Attempts} attempts.",
                    item.Id,
                    item.AttemptCount);
                continue;
            }

            try
            {
                using var content = CreateJsonContent(item);
                using var response = await client.PostAsync("api/alerts", content, cancellationToken)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation(
                        "Delivered cloud alert for client event {ClientEventId} (HTTP {Status}).",
                        item.ClientEventId,
                        (int)response.StatusCode);
                    continue;
                }

                var status = response.StatusCode;
                if (CloudAlertDeliveryPolicy.IsPermanentFailure(status))
                {
                    var body = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
                    item.LastError = $"HTTP {(int)status}: {body}";
                    var serialized = JsonSerializer.Serialize(item, SignalJson.Options);
                    await outboxStore.AppendDeadLetterAsync(serialized, "permanentHttp", cancellationToken)
                        .ConfigureAwait(false);
                    logger.LogWarning(
                        "Cloud alert {OutboxId} dead-lettered with permanent HTTP {Status}.",
                        item.Id,
                        (int)status);
                    continue;
                }

                item.AttemptCount++;
                item.LastError = $"HTTP {(int)status}";
                var referenceUtc = DateTimeOffset.UtcNow;
                var next = CloudAlertRetrySchedule.ComputeNextAttemptUtc(
                    item.AttemptCount,
                    referenceUtc,
                    TimeSpan.FromSeconds(Math.Max(1, options.InitialRetryDelaySeconds)),
                    TimeSpan.FromSeconds(Math.Max(1, options.MaxRetryDelaySeconds)));
                item.NextAttemptUtc = CloudAlertRetrySchedule.WithJitter(next, TimeSpan.FromSeconds(2), JitterRandom);
                remaining.Add(item);
            }
            catch (HttpRequestException ex)
            {
                item.AttemptCount++;
                item.LastError = ex.Message;
                var referenceUtc = DateTimeOffset.UtcNow;
                item.NextAttemptUtc = CloudAlertRetrySchedule.WithJitter(
                    CloudAlertRetrySchedule.ComputeNextAttemptUtc(
                        item.AttemptCount,
                        referenceUtc,
                        TimeSpan.FromSeconds(Math.Max(1, options.InitialRetryDelaySeconds)),
                        TimeSpan.FromSeconds(Math.Max(1, options.MaxRetryDelaySeconds))),
                    TimeSpan.FromSeconds(2),
                    JitterRandom);
                remaining.Add(item);
                logger.LogWarning(ex, "Transient HTTP error delivering cloud alert {OutboxId}.", item.Id);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                item.AttemptCount++;
                item.LastError = "timeout";
                var referenceUtc = DateTimeOffset.UtcNow;
                item.NextAttemptUtc = CloudAlertRetrySchedule.WithJitter(
                    CloudAlertRetrySchedule.ComputeNextAttemptUtc(
                        item.AttemptCount,
                        referenceUtc,
                        TimeSpan.FromSeconds(Math.Max(1, options.InitialRetryDelaySeconds)),
                        TimeSpan.FromSeconds(Math.Max(1, options.MaxRetryDelaySeconds))),
                    TimeSpan.FromSeconds(2),
                    JitterRandom);
                remaining.Add(item);
                logger.LogWarning(ex, "Timeout delivering cloud alert {OutboxId}.", item.Id);
            }
            catch (IOException ex)
            {
                item.AttemptCount++;
                item.LastError = ex.Message;
                var referenceUtc = DateTimeOffset.UtcNow;
                item.NextAttemptUtc = CloudAlertRetrySchedule.WithJitter(
                    CloudAlertRetrySchedule.ComputeNextAttemptUtc(
                        item.AttemptCount,
                        referenceUtc,
                        TimeSpan.FromSeconds(Math.Max(1, options.InitialRetryDelaySeconds)),
                        TimeSpan.FromSeconds(Math.Max(1, options.MaxRetryDelaySeconds))),
                    TimeSpan.FromSeconds(2),
                    JitterRandom);
                remaining.Add(item);
                logger.LogWarning(ex, "IO error delivering cloud alert {OutboxId}.", item.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                item.AttemptCount++;
                item.LastError = ex.Message;
                var referenceUtc = DateTimeOffset.UtcNow;
                item.NextAttemptUtc = CloudAlertRetrySchedule.WithJitter(
                    CloudAlertRetrySchedule.ComputeNextAttemptUtc(
                        item.AttemptCount,
                        referenceUtc,
                        TimeSpan.FromSeconds(Math.Max(1, options.InitialRetryDelaySeconds)),
                        TimeSpan.FromSeconds(Math.Max(1, options.MaxRetryDelaySeconds))),
                    TimeSpan.FromSeconds(2),
                    JitterRandom);
                remaining.Add(item);
                logger.LogWarning(ex, "Unexpected error delivering cloud alert {OutboxId}.", item.Id);
            }
        }

        await outboxStore.ReplacePendingAsync(remaining, cancellationToken).ConfigureAwait(false);
    }

    private static StringContent CreateJsonContent(CloudAlertOutboxItem item)
    {
        var payload = new
        {
            externalDeviceId = item.ExternalDeviceId,
            sourceIp = item.SourceIp,
            destinationPort = item.DestinationPort,
            service = item.Service,
            simulateAcknowledgeAtEscalationOrder = (int?)null,
            clientEventId = item.ClientEventId
        };

        var json = JsonSerializer.Serialize(payload, SignalJson.Options);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }
}
