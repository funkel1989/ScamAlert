using Microsoft.EntityFrameworkCore;
using ScamAlert.Api.Services.Alerts;
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
builder.Services.AddScoped<AlertWorkflowService>();
builder.Services.AddScoped<INotificationGateway, LoggingNotificationGateway>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ScamAlertDbContext>();
    dbContext.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
