using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScamAlert.Api.Contracts;
using ScamAlert.Api.Services.Portal;
using ScamAlert.Api.Services.Signup;
using ScamAlert.Data;

namespace ScamAlert.Api.Tests;

public sealed class PortalManagementTests
{
    [Fact]
    public async Task Portal_device_service_create_list_and_rotate()
    {
        await using var factory = new TestWebApplicationFactory();
        var customerId = await SignupCustomerAsync(factory, "portal-devices@example.com", "device-portal-1");

        await using var scope = factory.Services.CreateAsyncScope();
        var devices = scope.ServiceProvider.GetRequiredService<IPortalDeviceService>();

        var created = await devices.CreateAsync(customerId, new CreatePortalDeviceRequest("Kitchen PC", null), default);
        Assert.False(string.IsNullOrWhiteSpace(created.IngestApiKey));

        var list = await devices.ListAsync(customerId, default);
        Assert.Equal(2, list.Count);

        var rotated = await devices.RotateIngestKeyAsync(customerId, created.Id, default);
        Assert.NotNull(rotated);
        Assert.NotEqual(created.IngestApiKey, rotated!.IngestApiKey);
    }

    [Fact]
    public async Task Portal_contact_service_crud()
    {
        await using var factory = new TestWebApplicationFactory();
        var customerId = await SignupCustomerAsync(factory, "portal-contacts@example.com", "device-portal-2");

        await using var scope = factory.Services.CreateAsyncScope();
        var contacts = scope.ServiceProvider.GetRequiredService<IPortalContactService>();

        var created = await contacts.CreateAsync(
            customerId,
            new CreatePortalContactRequest("Neighbor", "+15555550999", 3),
            default);

        var updated = await contacts.UpdateAsync(
            customerId,
            created.Id,
            new UpdatePortalContactRequest("Neighbor Pat", "+15555550998", 3, true),
            default);
        Assert.NotNull(updated);
        Assert.Equal("Neighbor Pat", updated!.FullName);

        Assert.True(await contacts.DeleteAsync(customerId, created.Id, default));
    }

    [Fact]
    public async Task Signup_returns_provisioned_device_keys()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/signup",
            new
            {
                name = "Keys Test",
                email = "signup-keys@example.com",
                password = "LongPassw0rd!",
                planCode = "pro",
                contacts = new[] { new { fullName = "A", phoneNumber = "+15555550111", escalationOrder = 1 } },
                devices = new[] { new { deviceName = "PC", externalDeviceId = "device-keys-1" } },
                consents = SignupTestHelpers.SignupConsentsJson()
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(doc.TryGetProperty("provisionedDevices", out var devices));
        Assert.Equal(1, devices.GetArrayLength());
        Assert.False(string.IsNullOrWhiteSpace(devices[0].GetProperty("ingestApiKey").GetString()));
    }

    [Fact]
    public async Task Password_reset_creates_token_row()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync(
            "/api/signup",
            new
            {
                name = "Reset User",
                email = "reset-user@example.com",
                password = "LongPassw0rd!",
                planCode = "pro",
                contacts = new[] { new { fullName = "A", phoneNumber = "+15555550112", escalationOrder = 1 } },
                devices = new[] { new { deviceName = "PC", externalDeviceId = "device-reset-1" } },
                consents = SignupTestHelpers.SignupConsentsJson()
            });

        await client.PostAsJsonAsync("/api/account/forgot-password", new { email = "reset-user@example.com" });

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ScamAlertDbContext>();
        var tokenRow = await db.PasswordResetTokens.AsNoTracking()
            .Where(x => x.Username == "reset-user@example.com" && !x.IsUsed)
            .OrderByDescending(x => x.CreatedUtc)
            .FirstAsync();

        Assert.False(string.IsNullOrWhiteSpace(tokenRow.TokenHash));
    }

    internal static async Task<Guid> SignupCustomerAsync(
        TestWebApplicationFactory factory,
        string email,
        string externalDeviceId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var signup = scope.ServiceProvider.GetRequiredService<ISignupService>();
        var result = await signup.RegisterAndStartCheckoutAsync(
            new SelfServeSignupRequest(
                "Portal User",
                email,
                "LongPassw0rd!",
                "pro",
                [new CreateContactRequest("Primary", "+15555550123", 1)],
                [new CreateDeviceRequest("PC", externalDeviceId)],
                SignupTestHelpers.ValidConsents()),
            default);

        Assert.Single(result.ProvisionedDevices);
        return result.CustomerId;
    }
}
