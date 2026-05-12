var builder = DistributedApplication.CreateBuilder(args);

// Password must be a parameter resource (or null for an auto-generated password).
var sqlPassword = builder.AddParameter("sql-password", "ScamAlert_Dev_2026!", publishValueAsDefault: false, secret: false);
var sql = builder.AddSqlServer("sql", sqlPassword);
var scamDb = sql.AddDatabase("ScamAlertDb");

builder.AddProject<Projects.ScamAlert_Api>("api")
    .WithReference(scamDb)
    .WaitFor(sql)
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development");

builder.Build().Run();
