using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ScamAlert.Api.Contracts;
using ScamAlert.Api.Services.Pairing;
using ScamAlert.Api.Services.Portal;

namespace ScamAlert.Api.Tests;

public sealed class DevicePairingTests
{
    [Fact]
    public async Task Pairing_code_can_be_created_and_redeemed_once()
    {
        await using var factory = new TestWebApplicationFactory();
        var customerId = await PortalManagementTests.SignupCustomerAsync(
            factory,
            "pairing-test@example.com",
            "device-pairing-1");

        await using var scope = factory.Services.CreateAsyncScope();
        var devices = scope.ServiceProvider.GetRequiredService<IPortalDeviceService>();
        var pairing = scope.ServiceProvider.GetRequiredService<IDevicePairingService>();

        var created = await devices.CreateAsync(customerId, new CreatePortalDeviceRequest("Living room PC", null), default);
        var codeResponse = await pairing.CreateCodeAsync(customerId, created.Id, default);

        Assert.NotNull(codeResponse);
        Assert.Equal(8, codeResponse!.Code.Length);

        var redeemed = await pairing.RedeemAsync(codeResponse.Code, default);
        Assert.NotNull(redeemed);
        Assert.Equal(created.ExternalDeviceId, redeemed!.ExternalDeviceId);
        Assert.False(string.IsNullOrWhiteSpace(redeemed.DeviceIngestApiKey));

        var secondAttempt = await pairing.RedeemAsync(codeResponse.Code, default);
        Assert.Null(secondAttempt);
    }

    [Fact]
    public async Task Setup_redeem_endpoint_returns_credentials()
    {
        await using var factory = new TestWebApplicationFactory();
        var customerId = await PortalManagementTests.SignupCustomerAsync(
            factory,
            "pairing-api@example.com",
            "device-pairing-2");

        await using var setupScope = factory.Services.CreateAsyncScope();
        var pairing = setupScope.ServiceProvider.GetRequiredService<IDevicePairingService>();
        var devices = setupScope.ServiceProvider.GetRequiredService<IPortalDeviceService>();
        var created = await devices.CreateAsync(customerId, new CreatePortalDeviceRequest("Office PC", null), default);
        var code = await pairing.CreateCodeAsync(customerId, created.Id, default);

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/setup/redeem",
            new { code = code!.Code });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<DevicePairingRedeemResponse>();
        Assert.NotNull(body);
        Assert.Equal(created.ExternalDeviceId, body!.ExternalDeviceId);
    }
}
