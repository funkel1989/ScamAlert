using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ScamAlert.Api.Services.Alerts;
using ScamAlert.Api.Services.Billing;
using ScamAlert.Api.Services.Notifications;
using ScamAlert.Api.Services.Stripe;

namespace ScamAlert.Api.Tests;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string MasterConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=master;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    public CountingNotificationGateway CountingGateway { get; } = new();

    private readonly string testDatabaseName = $"ScamAlertApiTests_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connectionString =
            $"Server=(localdb)\\mssqllocaldb;Database={testDatabaseName};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:ScamAlertDb", connectionString);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INotificationGateway>();
            services.AddSingleton<INotificationGateway>(CountingGateway);
            services.PostConfigure<StripeOptions>(o =>
            {
                o.SkipPaymentForDevelopment = true;
            });
            services.PostConfigure<BillingOptions>(o =>
            {
                o.Tiers =
                [
                    new BillingTierOptions
                    {
                        PlanCode = "pro",
                        StripePriceId = "price_test_799",
                        DisplayName = "Personal license",
                        MonthlyPriceLabel = "$7.99 per month (USD)"
                    }
                ];
            });
            services.PostConfigure<AlertsOptions>(o =>
            {
                o.EscalationDelaySeconds = 1;
                o.EscalationPollIntervalSeconds = 60;
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            TryDropDatabase();
        }
    }

    private void TryDropDatabase()
    {
        try
        {
            using var conn = new SqlConnection(MasterConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                IF DB_ID(N'{testDatabaseName.Replace("'", "''", StringComparison.Ordinal)}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{testDatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{testDatabaseName}];
                END
                """;
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // best-effort cleanup; LocalDB may be unavailable
        }
    }
}

public sealed class CountingNotificationGateway : INotificationGateway
{
    private int notifyCallCount;

    public int NotifyCallCount => Volatile.Read(ref notifyCallCount);

    public Task<GatewayResult> NotifyContactAsync(
        ContactNotification notification,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref notifyCallCount);
        return Task.FromResult(new GatewayResult(false, null, "test"));
    }
}
