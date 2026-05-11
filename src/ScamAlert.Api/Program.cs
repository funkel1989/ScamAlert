using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScamAlert.Api.HostedServices;
using ScamAlert.Api.Services.Alerts;
using ScamAlert.Api.Services.Audit;
using ScamAlert.Api.Services.Auth;
using ScamAlert.Api.Services.Notifications;
using ScamAlert.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddDbContext<ScamAlertDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("ScamAlertDb")
        ?? "Data Source=scamalert.db";
    options.UseSqlite(connectionString);
});
builder.Services.Configure<AlertsOptions>(builder.Configuration.GetSection(AlertsOptions.SectionName));
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

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ScamAlertDbContext>();
    dbContext.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
