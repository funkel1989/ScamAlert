using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScamAlert.Api.Services.Auth;
using ScamAlert.Data;
using ScamAlert.Data.Entities;

namespace ScamAlert.Api.HostedServices;

public sealed class AuthBootstrapHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<AuthOptions> authOptions,
    IHostEnvironment hostEnvironment,
    ILogger<AuthBootstrapHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = authOptions.Value;
        if (!opts.BootstrapAdmin.Enabled)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScamAlertDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var hasUsers = await dbContext.AuthUserCredentials.AnyAsync(cancellationToken);
        if (hasUsers)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(opts.BootstrapAdmin.Username) ||
            string.IsNullOrWhiteSpace(opts.BootstrapAdmin.Password))
        {
            if (!hostEnvironment.IsDevelopment())
            {
                logger.LogWarning("Bootstrap admin is enabled but credentials are missing.");
            }

            return;
        }

        var now = DateTimeOffset.UtcNow;
        dbContext.AuthUserCredentials.Add(new AuthUserCredential
        {
            Id = Guid.NewGuid(),
            Username = opts.BootstrapAdmin.Username.Trim(),
            PasswordHash = passwordHasher.HashPassword(opts.BootstrapAdmin.Password),
            RolesCsv = string.Join(",", opts.BootstrapAdmin.Roles ?? ["admin", "operator"]),
            CustomerScopeCsv = "*",
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Bootstrapped initial admin user.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
