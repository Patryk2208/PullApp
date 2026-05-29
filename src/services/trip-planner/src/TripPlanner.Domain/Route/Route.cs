using TripPlanner.Domain.Compute;

namespace TripPlanner.Domain.Route;

public enum RouteStatus { Calculating, Created, Active, Full }

public class Route
{
    public Guid Id { get; private set; }
    public Guid DriverId { get; private set; }
    public RouteStatus Status { get; private set; }
    public GeoPoint Start { get; private set; } = default!;
    public GeoPoint End { get; private set; } = default!;
    public GeoPoint? CurrentLocation { get; private set; }
    public int Capacity { get; private set; }
    public int ActiveRideCount { get; private set; }

    // Populated by route-calc once the async job completes.
    public string? GeometryJson { get; private set; }
    public int? EtaSeconds { get; private set; }
    public int? DistanceMeters { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ActivatedAt { get; private set; }

    private Route() { }

    public static Route Create(Guid driverId, GeoPoint start, GeoPoint end, int capacity) =>
        new()
        {
            Id = Guid.NewGuid(),
            DriverId = driverId,
            Status = RouteStatus.Calculating,
            Start = start,
            End = end,
            Capacity = capacity,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    // Called when route-calc returns the computed geometry (flow 0 completion).
    public void SetGeometry(string geometryJson, int etaSeconds, int distanceMeters)
    {
        GeometryJson = geometryJson;
        EtaSeconds = etaSeconds;
        DistanceMeters = distanceMeters;
        Status = RouteStatus.Created;
    }

    // Flow 1: driver activates route. Application layer must validate location ≈ Start before calling.
    public void Activate(GeoPoint currentLocation)
    {
        Status = RouteStatus.Active;
        CurrentLocation = currentLocation;
        ActivatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateLocation(GeoPoint location) => CurrentLocation = location;

    // Flow 5: atomically add a ride and possibly mark route as full.
    // Returns true if the ride was added, false if route is already full.
    public bool TryAddRide()
    {
        if (ActiveRideCount >= Capacity) return false;
        ActiveRideCount++;
        if (ActiveRideCount >= Capacity) Status = RouteStatus.Full;
        return true;
    }

    // Flow 8/1.5: remove a ride (passenger cancelled or ride ended).
    public void RemoveRide()
    {
        if (ActiveRideCount > 0) ActiveRideCount--;
        if (Status == RouteStatus.Full) Status = RouteStatus.Active;
    }
}
