using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScamAlert.Api.Tests;

public sealed class AlertsIngestAuthTests
{
    [Fact]
    public async Task Raise_without_ingest_key_and_without_bearer_is_unauthorized()
    {
        using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();

        const string deviceId = "device-auth-1";
        var createResponse = await client.PostAsJsonAsync(
            "/api/customers",
            new
            {
                name = "Auth Co",
                email = "auth@example.com",
                planCode = "pro",
                contacts = new[]
                {
                    new { fullName = "Primary", phoneNumber = "+15555550100", escalationOrder = 1 }
                },
                devices = new[] { new { deviceName = "PC", externalDeviceId = deviceId } }
            });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var alertResponse = await client.PostAsJsonAsync(
            "/api/alerts",
            new
            {
                externalDeviceId = deviceId,
                sourceIp = "198.51.100.9",
                destinationPort = 3389,
                service = "rdp",
                simulateAcknowledgeAtEscalationOrder = (int?)null,
                clientEventId = (Guid?)null
            });

        Assert.Equal(HttpStatusCode.Unauthorized, alertResponse.StatusCode);
    }

    [Fact]
    public async Task Raise_with_valid_ingest_key_is_created()
    {
        using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();

        const string deviceId = "device-auth-2";
        var createResponse = await client.PostAsJsonAsync(
            "/api/customers",
            new
            {
                name = "Auth Co 2",
                email = "auth2@example.com",
                planCode = "pro",
                contacts = new[]
                {
                    new { fullName = "Primary", phoneNumber = "+15555550100", escalationOrder = 1 }
                },
                devices = new[] { new { deviceName = "PC", externalDeviceId = deviceId } }
            });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createDoc = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var ingestApiKey = createDoc.GetProperty("devices")[0].GetProperty("ingestApiKey").GetString();
        Assert.False(string.IsNullOrWhiteSpace(ingestApiKey));

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/alerts")
        {
            Content = JsonContent.Create(
                new
                {
                    externalDeviceId = deviceId,
                    sourceIp = "198.51.100.10",
                    destinationPort = 22,
                    service = "ssh",
                    simulateAcknowledgeAtEscalationOrder = (int?)null,
                    clientEventId = (Guid?)null
                })
        };
        request.Headers.TryAddWithoutValidation("X-ScamAlert-DeviceKey", ingestApiKey);

        var alertResponse = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, alertResponse.StatusCode);
    }
}
