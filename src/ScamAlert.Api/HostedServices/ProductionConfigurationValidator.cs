using Microsoft.Extensions.Options;
using ScamAlert.Api.Services.Auth;
using ScamAlert.Api.Services.Billing;
using ScamAlert.Api.Services.Email;
using ScamAlert.Api.Services.Stripe;
using ScamAlert.Api.Services.Web;

namespace ScamAlert.Api.HostedServices;

/// <summary>Logs production misconfiguration at startup (does not block the process).</summary>
public sealed class ProductionConfigurationValidator(
    IHostEnvironment environment,
    IOptions<AuthOptions> authOptions,
    IOptions<StripeOptions> stripeOptions,
    IOptions<WebSiteOptions> webOptions,
    IOptions<EmailOptions> emailOptions,
    IOptions<BillingOptions> billingOptions,
    ILogger<ProductionConfigurationValidator> logger) : IHostedService
{
    private static readonly string[] PlaceholderJwtMarkers =
        ["CHANGE_ME", "REPLACE", "<CHANGE_ME"];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsProduction())
        {
            return Task.CompletedTask;
        }

        var issues = new List<string>();
        var jwtKey = authOptions.Value.Jwt.SigningKey ?? string.Empty;
        if (PlaceholderJwtMarkers.Any(m => jwtKey.Contains(m, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add("Authentication:Jwt:SigningKey is still a placeholder.");
        }

        if (authOptions.Value.BootstrapAdmin.Enabled)
        {
            issues.Add("Authentication:BootstrapAdmin:Enabled must be false in production.");
        }

        if (!stripeOptions.Value.SkipPaymentForDevelopment)
        {
            if (string.IsNullOrWhiteSpace(stripeOptions.Value.SecretKey))
            {
                issues.Add("Stripe:SecretKey is required when SkipPaymentForDevelopment is false.");
            }

            foreach (var tier in billingOptions.Value.Tiers)
            {
                if (tier.StripePriceId.Contains("REPLACE", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"Billing tier '{tier.PlanCode}' StripePriceId is still a placeholder.");
                }
            }
        }

        if (!webOptions.Value.PublicBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("Web:PublicBaseUrl should use HTTPS in production.");
        }

        if (webOptions.Value.InstallerDownloadUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("Web:InstallerDownloadUrl still points at a placeholder URL.");
        }

        if (string.IsNullOrWhiteSpace(emailOptions.Value.SendGridApiKey))
        {
            issues.Add("Email:SendGridApiKey is empty — password reset and welcome emails will only log locally.");
        }

        if (issues.Count == 0)
        {
            logger.LogInformation("Production configuration validation passed.");
            return Task.CompletedTask;
        }

        foreach (var issue in issues)
        {
            logger.LogError("Production configuration: {Issue}", issue);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
