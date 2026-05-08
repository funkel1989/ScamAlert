using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ScamAlert.Api.Services.Alerts;
using ScamAlert.Api.Services.Notifications;

namespace ScamAlert.Api.Tests;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public CountingNotificationGateway CountingGateway { get; } = new();

    private readonly string dbPath = Path.Combine(
        Path.GetTempPath(),
        $"scamalert-api-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:ScamAlertDb", $"Data Source={dbPath}");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INotificationGateway>();
            services.AddSingleton<INotificationGateway>(CountingGateway);
            services.PostConfigure<AlertsOptions>(o =>
            {
                o.EscalationDelaySeconds = 1;
                o.EscalationPollIntervalSeconds = 60;
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
            catch
            {
                // best-effort cleanup of temp SQLite file
            }
        }

        base.Dispose(disposing);
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
