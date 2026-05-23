using TripPlanner.Domain.Compute;

namespace TripPlanner.Domain.Driver;

public enum DriverRouteStatus { Pending, Active, InRide, Disconnected, Cancelled, Completed }

// Represents the driver's declared route from A to B.
// One active route per driver at a time (enforced at the application layer).
public class DriverRoute
{
    public Guid Id { get; init; }
    public Guid DriverId { get; init; }
    public DriverRouteStatus Status { get; private set; } = DriverRouteStatus.Pending;

    public GeoPoint StartPoint { get; init; } = default!;
    public GeoPoint EndPoint { get; init; } = default!;

    // Populated once Route-Calc confirms the route.
    public string? RouteGeometryJson { get; private set; }
    public int? EtaSeconds { get; private set; }
    public int? DistanceMeters { get; private set; }

    // References the RouteJob that triggered Route-Calc for this route.
    public Guid? JobId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ActivatedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public void Activate(string routeGeometryJson, int etaSeconds, int distanceMeters)
    {
        Status = DriverRouteStatus.Active;
        RouteGeometryJson = routeGeometryJson;
        EtaSeconds = etaSeconds;
        DistanceMeters = distanceMeters;
        ActivatedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel()
    {
        Status = DriverRouteStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
    }

    // Called when the driver completes their last ride segment and their
    // route destination has been reached.
    public void Complete()
    {
        Status = DriverRouteStatus.Completed;
    }
}
