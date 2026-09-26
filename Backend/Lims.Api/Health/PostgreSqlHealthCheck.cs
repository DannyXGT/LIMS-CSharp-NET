using Lims.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Lims.Api.Health;

public sealed class PostgreSqlHealthCheck(
    LimsDbContext dbContext,
    ILogger<PostgreSqlHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (await dbContext.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                return HealthCheckResult.Healthy("PostgreSQL is reachable.");
            }

            ApiLog.DatabaseUnavailable(logger);
            return HealthCheckResult.Unhealthy("PostgreSQL is not reachable.");
        }
        catch (Exception exception)
        {
            ApiLog.DatabaseCheckFailed(logger, exception);
            return HealthCheckResult.Unhealthy("PostgreSQL readiness check failed.");
        }
    }
}
