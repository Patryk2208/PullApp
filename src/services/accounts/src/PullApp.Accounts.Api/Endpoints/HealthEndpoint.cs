namespace PullApp.Accounts.Api.Endpoints;

public class HealthEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/health", (ILogger<HealthEndpoint> logger) =>
        {
            logger.LogInformation("Health check called");
            return Results.Ok(new { status = "ok" });
        });

        app.MapGet("/health/live",  () => Results.Ok(new { status = "ok" }));
        app.MapGet("/health/ready", () => Results.Ok(new { status = "ok" }));
    }
}
