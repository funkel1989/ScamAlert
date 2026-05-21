using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScamAlert.Api.Contracts;
using ScamAlert.Api.Services.Auth;
using ScamAlert.Api.Services.Billing;
using ScamAlert.Data;
using ScamAlert.Data.Entities;
using ScamAlert.Data.Enums;

namespace ScamAlert.Api.Tests;

public sealed class CustomerBillingServiceTests
{
    [Fact]
    public async Task GetSummary_returns_null_for_global_admin_principal()
    {
        await using var factory = new TestWebApplicationFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var billing = scope.ServiceProvider.GetRequiredService<ICustomerBillingService>();
        var principal = PortalClaimsPrincipalFactory.Create("admin", ["admin"], ["*"]);

        var summary = await billing.GetSummaryAsync(principal, default);

        Assert.Null(summary);
    }

    [Fact]
    public async Task GetSummary_exposes_single_seven_ninety_nine_tier()
    {
        await using var factory = new TestWebApplicationFactory();
        var customerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ScamAlertDbContext>();
            db.Customers.Add(new Customer
            {
                Id = customerId,
                Name = "Billing Test",
                Email = "billing-plan-test@example.com",
                CreatedUtc = now,
                UpdatedUtc = now,
                Subscriptions =
                [
                    new Subscription
                    {
                        Id = Guid.NewGuid(),
                        CustomerId = customerId,
                        PlanCode = "pro",
                        Status = SubscriptionStatus.Active,
                        StartsUtc = now,
                        CreatedUtc = now,
                        UpdatedUtc = now
                    }
                ]
            });
            await db.SaveChangesAsync();
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var billing = scope.ServiceProvider.GetRequiredService<ICustomerBillingService>();
        var principal = PortalClaimsPrincipalFactory.Create(
            "billing-plan-test@example.com",
            ["operator"],
            [customerId.ToString("D")]);

        var summary = await billing.GetSummaryAsync(principal, default);

        Assert.NotNull(summary);
        Assert.Equal("pro", summary.PlanCode);
        Assert.Single(summary.AvailableTiers);
        Assert.Equal("Personal license", summary.AvailableTiers[0].DisplayName);
    }

    [Fact]
    public async Task UpdateBillingAddress_persists_to_customer()
    {
        await using var factory = new TestWebApplicationFactory();
        var customerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ScamAlertDbContext>();
            db.Customers.Add(new Customer
            {
                Id = customerId,
                Name = "Address Test",
                Email = "billing-address@example.com",
                CreatedUtc = now,
                UpdatedUtc = now
            });
            await db.SaveChangesAsync();
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var billing = scope.ServiceProvider.GetRequiredService<ICustomerBillingService>();
        var principal = PortalClaimsPrincipalFactory.Create(
            "billing-address@example.com",
            ["operator"],
            [customerId.ToString("D")]);

        await billing.UpdateBillingAddressAsync(
            principal,
            new UpdateBillingAddressRequest("123 Main St", "Suite 2", "Wilmington", "DE", "19801"),
            default);

        var db2 = scope.ServiceProvider.GetRequiredService<ScamAlertDbContext>();
        var customer = await db2.Customers.AsNoTracking().SingleAsync(c => c.Id == customerId);
        Assert.Equal("123 Main St", customer.BillingLine1);
        Assert.Equal("Suite 2", customer.BillingLine2);
        Assert.Equal("Wilmington", customer.BillingCity);
        Assert.Equal("DE", customer.BillingState);
        Assert.Equal("19801", customer.BillingPostalCode);
        Assert.NotNull(customer.BillingAddressSyncedUtc);

        var summary = await billing.GetSummaryAsync(principal, default);
        Assert.NotNull(summary?.BillingAddress);
        Assert.Equal("19801", summary.BillingAddress.PostalCode);
    }

    [Fact]
    public async Task Billing_summary_api_returns_forbidden_for_testing_admin_user()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/billing/summary");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
