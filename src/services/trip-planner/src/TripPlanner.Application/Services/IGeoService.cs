using TripPlanner.Domain.Compute;

namespace TripPlanner.Application.Services;

public interface IGeoService
{
    Task<bool> IsWithinServiceAreaAsync(GeoPoint point, CancellationToken ct);
}
