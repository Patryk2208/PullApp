using System.Text.Json;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Compute;

namespace TripPlanner.Application.Features;

public class RouteComputedHandler(ISseHub hub) : IHandler<ComputeJob>
{
    public async Task HandleAsync(ComputeJob result, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(result);
        await hub.PushAsync(result.RequestingUserId, "drier_route_computed", json, ct);
    }
}