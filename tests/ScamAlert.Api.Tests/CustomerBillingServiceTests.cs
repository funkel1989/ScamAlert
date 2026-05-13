using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task Change_plan_updates_plan_code_when_skip_payment()
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

        await billing.ChangePlanAsync(principal, "family", default);

        var db2 = scope.ServiceProvider.GetRequiredService<ScamAlertDbContext>();
        var sub = await db2.Subscriptions.AsNoTracking().SingleAsync(x => x.CustomerId == customerId);
        Assert.Equal("family", sub.PlanCode);
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
