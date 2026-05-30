using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;

namespace TripPlanner.Api.Endpoints;

public class HealthEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = HealthJson.Write
        });

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = HealthJson.Write
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("ready"),
            ResponseWriter = HealthJson.Write
        });
    }
}

internal static class HealthJson
{
    internal static Task Write(HttpContext ctx, HealthReport report)
    {
        ctx.Response.ContentType = "application/json";
        var body = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString().ToLowerInvariant(),
            total_duration_ms = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.ToDictionary(
                e => e.Key,
                e => (object)new
                {
                    status      = e.Value.Status.ToString().ToLowerInvariant(),
                    description = e.Value.Description,
                    duration_ms = e.Value.Duration.TotalMilliseconds,
                    exception   = e.Value.Exception?.Message
                })
        });
        return ctx.Response.WriteAsync(body);
    }
}
