using Microsoft.Extensions.Diagnostics.HealthChecks;
using TripPlanner.Infrastructure.Queue;

namespace TripPlanner.Api.Checks;

public class RabbitHealthCheck(RabbitConnection rabbit) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var conn = await rabbit.GetAsync(ct);
            return conn.IsOpen
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("RabbitMQ connection is closed");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(ex.Message);
        }
    }
}
