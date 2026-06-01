using Confluent.Kafka;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TripPlanner.Infrastructure.Kafka;

namespace TripPlanner.Api.Checks;

public class KafkaHealthCheck(IOptions<KafkaOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var admin = new AdminClientBuilder(new AdminClientConfig
            {
                BootstrapServers = options.Value.BootstrapServers,
            }).Build();

            var metadata = admin.GetMetadata(timeout: TimeSpan.FromSeconds(5));
            return Task.FromResult(HealthCheckResult.Healthy(
                $"brokers={metadata.Brokers.Count} topics={metadata.Topics.Count}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(ex.Message));
        }
    }
}