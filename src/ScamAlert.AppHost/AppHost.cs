var builder = DistributedApplication.CreateBuilder(args);

// Password must be a parameter resource (or null for an auto-generated password).
var sqlPassword = builder.AddParameter("sql-password", "ScamAlert_Dev_2026!", publishValueAsDefault: false, secret: false);
// Dev-only SQL browser (like pgAdmin for Postgres). Not deployed — starts with AppHost.
var sql = builder.AddSqlServer("sql", sqlPassword)
    .WithDbGate(dbgate => dbgate.WithUrl("/", "SQL queries"));
var scamDb = sql.AddDatabase("ScamAlertDb");

var api = builder.AddProject<Projects.ScamAlert_Api>("api")
    .WithReference(scamDb)
    .WaitFor(sql)
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
    .WithHttpHealthCheck("/api/health");

api.WithUrl("/scalar", "Scalar API");
api.WithUrl("/", "Website");

builder.Build().Run();
