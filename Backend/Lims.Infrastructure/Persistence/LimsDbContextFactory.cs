using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lims.Infrastructure.Persistence;

public sealed class LimsDbContextFactory : IDesignTimeDbContextFactory<LimsDbContext>
{
    private const string DesignTimeConnection =
        "Host=localhost;Database=lims_design;Username=lims_design;Password=not-used-by-migrations";

    public LimsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("LIMS_DB_CONNECTION_STRING");
        var options = new DbContextOptionsBuilder<LimsDbContext>()
            .UseNpgsql(string.IsNullOrWhiteSpace(connectionString) ? DesignTimeConnection : connectionString)
            .Options;

        return new LimsDbContext(options);
    }
}
