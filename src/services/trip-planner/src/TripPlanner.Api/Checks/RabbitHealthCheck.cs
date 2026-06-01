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
            if (!conn.IsOpen)
                return HealthCheckResult.Unhealthy("connection is closed");

            var ep = conn.Endpoint;
            return HealthCheckResult.Healthy($"{ep.HostName}:{ep.Port}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(ex.Message);
        }
    }
}
