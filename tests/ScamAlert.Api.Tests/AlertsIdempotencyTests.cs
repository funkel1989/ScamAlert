using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScamAlert.Api.Tests;

public sealed class AlertsIdempotencyTests
{
    [Fact]
    public async Task Duplicate_raise_with_same_client_event_id_does_not_notify_again()
    {
        using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();

        const string deviceId = "device-idem-1";
        var createResponse = await client.PostAsJsonAsync(
            "/api/customers",
            new
            {
                name = "Idem Co",
                email = "idem@example.com",
                planCode = "pro",
                contacts = new[]
                {
                    new { fullName = "A", phoneNumber = "+15555550100", escalationOrder = 1 }
                },
                devices = new[] { new { deviceName = "PC", externalDeviceId = deviceId } }
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var clientEventId = Guid.NewGuid();
        var body = new
        {
            externalDeviceId = deviceId,
            sourceIp = "203.0.113.10",
            destinationPort = 3389,
            service = "rdp",
            simulateAcknowledgeAtEscalationOrder = (int?)null,
            clientEventId
        };

        var first = await client.PostAsJsonAsync("/api/alerts", body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(1, factory.CountingGateway.NotifyCallCount);

        var second = await client.PostAsJsonAsync("/api/alerts", body);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(1, factory.CountingGateway.NotifyCallCount);

        var firstDoc = await first.Content.ReadFromJsonAsync<JsonElement>();
        var secondDoc = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(firstDoc.GetProperty("id").GetGuid(), secondDoc.GetProperty("id").GetGuid());
    }
}
