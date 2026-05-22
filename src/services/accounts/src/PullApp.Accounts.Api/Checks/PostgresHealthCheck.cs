using Microsoft.Extensions.Diagnostics.HealthChecks;
using PullApp.Accounts.Infrastructure.Persistence;

namespace PullApp.Accounts.Api.Checks;

public class PostgresHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var ok = await db.Database.CanConnectAsync(ct);
            return ok
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Cannot reach PostgreSQL");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(ex.Message);
        }
    }
}
