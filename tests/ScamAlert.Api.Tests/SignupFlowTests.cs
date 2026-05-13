using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScamAlert.Api.Services.Signup;
using ScamAlert.Data;
using ScamAlert.Data.Entities;
using ScamAlert.Data.Enums;

namespace ScamAlert.Api.Tests;

public sealed class SignupFlowTests
{
    [Fact]
    public async Task Signup_with_skip_payment_sets_active_subscription()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/signup",
            new
            {
                name = "Family Test",
                email = "family-signup-test@example.com",
                password = "longpassword1",
                planCode = "pro",
                contacts = new[]
                {
                    new { fullName = "Mom", phoneNumber = "+15555550100", escalationOrder = 1 }
                },
                devices = new[] { new { deviceName = "Living room PC", externalDeviceId = "device-signup-1" } }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var customerId = doc.GetProperty("customerId").GetGuid();
        Assert.Contains("signup/success", doc.GetProperty("checkoutUrl").GetString(), StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ScamAlertDbContext>();
        var sub = await db.Subscriptions.AsNoTracking().SingleAsync(x => x.CustomerId == customerId);
        Assert.Equal(SubscriptionStatus.Active, sub.Status);
    }

    [Fact]
    public async Task Signup_duplicate_email_returns_conflict()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();

        var body = new
        {
            name = "Dup",
            email = "dup-signup@example.com",
            password = "longpassword1",
            planCode = "pro",
            contacts = new[] { new { fullName = "A", phoneNumber = "+15555550101", escalationOrder = 1 } },
            devices = new[] { new { deviceName = "PC", externalDeviceId = "device-dup-1" } }
        };

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/signup", body)).StatusCode);
        var second = await client.PostAsJsonAsync("/api/signup", body);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Subscription_activator_flips_pending_to_active()
    {
        await using var factory = new TestWebApplicationFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ScamAlertDbContext>();
        var activator = scope.ServiceProvider.GetRequiredService<ISubscriptionPaymentActivator>();

        var customerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Customers.Add(new Customer
        {
            Id = customerId,
            Name = "Pending Co",
            Email = "pending@example.com",
            CreatedUtc = now,
            UpdatedUtc = now,
            Subscriptions =
            [
                new Subscription
                {
                    Id = Guid.NewGuid(),
                    CustomerId = customerId,
                    PlanCode = "pro",
                    Status = SubscriptionStatus.PendingPayment,
                    StartsUtc = now,
                    CreatedUtc = now,
                    UpdatedUtc = now
                }
            ]
        });
        await db.SaveChangesAsync();

        Assert.True(await activator.TryActivateCustomerAsync(customerId, CancellationToken.None));
        db.ChangeTracker.Clear();
        var status = await db.Subscriptions.AsNoTracking().Where(x => x.CustomerId == customerId).Select(x => x.Status).SingleAsync();
        Assert.Equal(SubscriptionStatus.Active, status);
    }

    [Fact]
    public async Task Stripe_webhook_without_secret_returns_not_found()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();
        var response = await client.PostAsync("/api/webhooks/stripe", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
