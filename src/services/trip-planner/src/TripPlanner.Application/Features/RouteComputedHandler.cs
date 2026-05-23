using System.Text.Json;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Compute;

namespace TripPlanner.Application.Features;

public class RouteComputedHandler(ISseHub hub) : IHandler<ComputeJobResult>
{
    public async Task HandleAsync(ComputeJobResult result, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(result);
        await hub.PushAsync(result.JobId, "drier_route_computed", json, ct);
    }
}