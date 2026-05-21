using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ScamAlert.Api.Components;
using ScamAlert.Api.HostedServices;
using ScamAlert.Api.Services.Alerts;
using ScamAlert.Api.Services.Billing;
using ScamAlert.Api.Services.Audit;
using ScamAlert.Api.Services.Auth;
using ScamAlert.Api.Services.Notifications;
using ScamAlert.Api.Services.Email;
using ScamAlert.Api.Services.Pairing;
using ScamAlert.Api.Services.Portal;
using ScamAlert.Api.Services.Signup;
using ScamAlert.Api.Services.Stripe;
using ScamAlert.Api.Services.Web;
using ScamAlert.Data;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "ScamAlert API";
        document.Info.Description = "ScamAlert API. In Development, POST /api/auth/token with bootstrap credentials for JWT.";
        return Task.CompletedTask;
    });
});
builder.Services.AddDbContext<ScamAlertDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("ScamAlertDb")
        ?? throw new InvalidOperationException(
            "Connection string 'ScamAlertDb' is not configured. Set ConnectionStrings:ScamAlertDb or run under Aspire.");
    options.UseSqlServer(connectionString);
});
builder.Services.AddDbContextFactory<ScamAlertDbContext>(
    options =>
    {
        var connectionString = builder.Configuration.GetConnectionString("ScamAlertDb")
            ?? throw new InvalidOperationException(
                "Connection string 'ScamAlertDb' is not configured. Set ConnectionStrings:ScamAlertDb or run under Aspire.");
        options.UseSqlServer(connectionString);
    },
    ServiceLifetime.Scoped);
builder.Services.Configure<AlertsOptions>(builder.Configuration.GetSection(AlertsOptions.SectionName));
builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection(StripeOptions.SectionName));
builder.Services.Configure<WebSiteOptions>(builder.Configuration.GetSection(WebSiteOptions.SectionName));
builder.Services.Configure<BillingOptions>(builder.Configuration.GetSection(BillingOptions.SectionName));
builder.Services.Configure<PairingOptions>(builder.Configuration.GetSection(PairingOptions.SectionName));
builder.Services.AddSingleton<IBillingTierCatalog, BillingTierCatalog>();
builder.Services.AddScoped<ISignupService, SignupService>();
builder.Services.AddScoped<ISignupCheckoutCompletionService, SignupCheckoutCompletionService>();
builder.Services.AddScoped<IPortalCookieSignInService, PortalCookieSignInService>();
builder.Services.AddScoped<ICustomerPortalContext, CustomerPortalContext>();
builder.Services.AddScoped<IPortalDeviceService, PortalDeviceService>();
builder.Services.AddScoped<IDevicePairingService, DevicePairingService>();
builder.Services.AddScoped<IPortalContactService, PortalContactService>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddHttpClient(nameof(SendGridEmailSender));
builder.Services.AddScoped<LoggingEmailSender>();
builder.Services.AddScoped<SendGridEmailSender>();
builder.Services.AddScoped<IEmailSender>(provider =>
{
    var email = provider.GetRequiredService<IOptions<EmailOptions>>().Value;
    return string.IsNullOrWhiteSpace(email.SendGridApiKey)
        ? provider.GetRequiredService<LoggingEmailSender>()
        : provider.GetRequiredService<SendGridEmailSender>();
});
builder.Services.AddScoped<ProvisionedDevicesSession>();
builder.Services.AddScoped<ICustomerBillingService, CustomerBillingService>();
builder.Services.AddScoped<ISubscriptionPaymentActivator, SubscriptionPaymentActivator>();
builder.Services.AddScoped<IStripeSubscriptionWebhookProcessor, StripeSubscriptionWebhookProcessor>();
builder.Services.AddScamAlertAuthentication(
    builder.Configuration,
    useTestingAuth: builder.Environment.IsEnvironment("Testing"));
builder.Services.AddScoped<AlertContactNotifier>();
builder.Services.AddScoped<AlertWorkflowService>();
builder.Services.AddScoped<AlertEscalationProcessor>();
builder.Services.AddHostedService<AuthBootstrapHostedService>();
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<AlertEscalationWorker>();
    builder.Services.AddHostedService<ProductionConfigurationValidator>();
}

builder.Services.Configure<TwilioOptions>(builder.Configuration.GetSection(TwilioOptions.SectionName));
builder.Services.AddHttpClient<TwilioNotificationGateway>();
builder.Services.AddScoped<ITwilioRequestValidator, TwilioRequestValidator>();
builder.Services.AddSingleton<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<LoggingNotificationGateway>();
builder.Services.AddScoped<INotificationGateway>(provider =>
{
    var twilioOptions = provider.GetRequiredService<IOptions<TwilioOptions>>().Value;
    var isConfigured = !string.IsNullOrWhiteSpace(twilioOptions.AccountSid)
        && !string.IsNullOrWhiteSpace(twilioOptions.AuthToken)
        && !string.IsNullOrWhiteSpace(twilioOptions.FromPhoneNumber);

    return isConfigured
        ? provider.GetRequiredService<TwilioNotificationGateway>()
        : provider.GetRequiredService<LoggingNotificationGateway>();
});

builder.Services.AddHttpContextAccessor();
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddCascadingAuthenticationState();
    builder.Services.AddScoped<HttpContextAuthenticationStateProvider>();
    builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
        sp.GetRequiredService<HttpContextAuthenticationStateProvider>());
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();
    builder.Services.AddScoped(sp =>
    {
        var accessor = sp.GetRequiredService<IHttpContextAccessor>();
        var http = accessor.HttpContext;
        return http is null
            ? new HttpClient()
            : new HttpClient { BaseAddress = new Uri($"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}") };
    });
}

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ScamAlertDbContext>();
    dbContext.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("ScamAlert API");
        options.WithOpenApiRoutePattern("/openapi/{documentName}.json");
    });
}

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
    app.UseStaticFiles();
}

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseAntiforgery();
}

app.MapDefaultEndpoints();
app.MapControllers();

app.MapGet("/signup/success", (HttpRequest request) =>
    Results.Redirect("/signup/complete" + request.QueryString.Value));

if (!app.Environment.IsEnvironment("Testing"))
{
    app.MapStaticAssets();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();
    app.MapGet("/logout", async (HttpContext ctx) =>
    {
        await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/");
    }).AllowAnonymous();
}

app.Run();

public partial class Program;
