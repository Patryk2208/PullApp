using TripPlanner.Domain.Compute;

namespace TripPlanner.Application.Services;

public interface IGeoService
{
    // Flow 0 / flow 3 guard: checks if a point lies within the configured service area polygon.
    Task<bool> IsWithinServiceAreaAsync(GeoPoint point, CancellationToken ct);

    // Flow 1 guard: checks if two points are within thresholdMeters of each other.
    // Used to validate current_location ≈ route start before activation.
    Task<bool> IsNearAsync(GeoPoint a, GeoPoint b, double thresholdMeters, CancellationToken ct);
}
