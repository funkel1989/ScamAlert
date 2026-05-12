using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ScamAlert.Data;

/// <summary>Design-time factory so migrations target SQL Server (matches Aspire dev database).</summary>
public sealed class ScamAlertDbContextFactory : IDesignTimeDbContextFactory<ScamAlertDbContext>
{
    public ScamAlertDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SCAMALERT_MIGRATIONS_CONNECTION")
            ?? "Server=(localdb)\\mssqllocaldb;Database=ScamAlertDesign;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True";

        var optionsBuilder = new DbContextOptionsBuilder<ScamAlertDbContext>();
        optionsBuilder.UseSqlServer(connectionString);
        return new ScamAlertDbContext(optionsBuilder.Options);
    }
}
