using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace TripPlanner.Api.Checks;

public class PostgresHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT version()";
            var version = await cmd.ExecuteScalarAsync(ct) as string;
            return HealthCheckResult.Healthy(version);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(ex.Message);
        }
    }
}
