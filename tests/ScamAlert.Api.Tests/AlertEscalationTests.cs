using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScamAlert.Api.Services.Alerts;
using ScamAlert.Data;

namespace ScamAlert.Api.Tests;

public sealed class AlertEscalationTests
{
    [Fact]
    public async Task Escalation_notifies_next_tier_after_delay()
    {
        using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();

        const string deviceId = "device-esc-1";
        var createResponse = await client.PostAsJsonAsync(
            "/api/customers",
            new
            {
                name = "Esc Co",
                email = "esc@example.com",
                planCode = "pro",
                contacts = new[]
                {
                    new { fullName = "Primary", phoneNumber = "+15555550100", escalationOrder = 1 },
                    new { fullName = "Secondary", phoneNumber = "+15555550101", escalationOrder = 2 }
                },
                devices = new[] { new { deviceName = "PC", externalDeviceId = deviceId } }
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createDoc = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var ingestApiKey = createDoc.GetProperty("devices")[0].GetProperty("ingestApiKey").GetString();
        Assert.False(string.IsNullOrWhiteSpace(ingestApiKey));

        var alertRequest = new HttpRequestMessage(HttpMethod.Post, "/api/alerts")
        {
            Content = JsonContent.Create(
            new
            {
                externalDeviceId = deviceId,
                sourceIp = "198.51.100.1",
                destinationPort = 22,
                service = "ssh",
                simulateAcknowledgeAtEscalationOrder = (int?)null,
                clientEventId = (Guid?)null
            })
        };
        alertRequest.Headers.TryAddWithoutValidation("X-ScamAlert-DeviceKey", ingestApiKey);
        var alertResponse = await client.SendAsync(alertRequest);

        Assert.Equal(HttpStatusCode.Created, alertResponse.StatusCode);
        Assert.Equal(1, factory.CountingGateway.NotifyCallCount);

        var alertDoc = await alertResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var alertId = alertDoc.GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScamAlertDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<AlertEscalationProcessor>();

        var alert = await db.AlertEvents
            .Include(a => a.NotificationAttempts)
            .SingleAsync(a => a.Id == alertId);

        var latestAttemptUtc = alert.NotificationAttempts.Max(a => a.AttemptedUtc);
        await processor.ProcessDueAlertsAsync(latestAttemptUtc.AddSeconds(2), CancellationToken.None);

        Assert.Equal(2, factory.CountingGateway.NotifyCallCount);
    }
}
