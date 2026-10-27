using Microsoft.Extensions.Diagnostics.HealthChecks;
using Runtivo.Api.Data;

namespace Runtivo.Api.Health;

public class DatabaseHealthCheck(RuntivoDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database non raggiungibile");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database non raggiungibile", ex);
        }
    }
}
