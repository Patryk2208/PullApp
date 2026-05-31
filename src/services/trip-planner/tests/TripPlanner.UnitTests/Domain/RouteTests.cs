namespace TripPlanner.UnitTests.Domain;

public class RouteTests
{
    private static Route NewRoute(int capacity = 3) =>
        Route.Create(Guid.NewGuid(), new GeoPoint(52.2, 21.0), new GeoPoint(52.3, 21.1), capacity);

    // ─── Create ──────────────────────────────────────────────────────────────

    [Fact]
    public void Create_SetsCalculatingStatusAndAllFields()
    {
        var driverId = Guid.NewGuid();
        var start    = new GeoPoint(52.2, 21.0);
        var end      = new GeoPoint(52.3, 21.1);

        var route = Route.Create(driverId, start, end, capacity: 2);

        Assert.NotEqual(Guid.Empty, route.Id);
        Assert.Equal(driverId,            route.DriverId);
        Assert.Equal(RouteStatus.Calculating, route.Status);
        Assert.Equal(start.Latitude,      route.Start.Latitude);
        Assert.Equal(end.Latitude,        route.End.Latitude);
        Assert.Equal(2,                   route.Capacity);
        Assert.Equal(0,                   route.ActiveRideCount);
        Assert.Null(route.RoutePoints);
        Assert.Null(route.CurrentLocation);
        Assert.Null(route.ActivatedAt);
    }

    // ─── SetGeometry ──────────────────────────────────────────────────────────

    [Fact]
    public void SetGeometry_TransitionsToCreatedAndStoresFields()
    {
        var route = NewRoute();
        var pts   = new[] { new GeoPoint(52.2, 21.0), new GeoPoint(52.3, 21.1) };

        route.SetGeometry(pts, durationSeconds: 600.0, distanceMeters: 10000.0);

        Assert.Equal(RouteStatus.Created, route.Status);
        Assert.Equal(2,                   route.RoutePoints!.Count);
        Assert.Equal(600.0,               route.DurationSeconds);
        Assert.Equal(10000.0,             route.DistanceMeters);
    }

    // ─── Activate ────────────────────────────────────────────────────────────

    [Fact]
    public void Activate_TransitionsToActiveAndSetsLocationAndTimestamp()
    {
        var route    = NewRoute();
        var location = new GeoPoint(52.2, 21.0);
        route.SetGeometry([new GeoPoint(52.2, 21.0), new GeoPoint(52.3, 21.1)], 300.0, 5000.0);

        route.Activate(location);

        Assert.Equal(RouteStatus.Active,    route.Status);
        Assert.Equal(location.Latitude,     route.CurrentLocation!.Latitude);
        Assert.NotNull(route.ActivatedAt);
    }

    // ─── UpdateLocation ───────────────────────────────────────────────────────

    [Fact]
    public void UpdateLocation_SetsCurrentLocation()
    {
        var route    = NewRoute();
        var location = new GeoPoint(52.25, 21.05);

        route.UpdateLocation(location);

        Assert.Equal(location.Latitude, route.CurrentLocation!.Latitude);
    }

    // ─── TryAddRide ───────────────────────────────────────────────────────────

    [Fact]
    public void TryAddRide_IncrementsActiveRideCount()
    {
        var route = NewRoute(capacity: 3);
        route.SetGeometry([new GeoPoint(52.2, 21.0), new GeoPoint(52.3, 21.1)], 300.0, 5000.0);
        route.Activate(new GeoPoint(52.2, 21.0));

        var result = route.TryAddRide();

        Assert.True(result);
        Assert.Equal(1, route.ActiveRideCount);
        Assert.Equal(RouteStatus.Active, route.Status);
    }

    [Fact]
    public void TryAddRide_WhenLastSlot_SetsStatusToFull()
    {
        var route = NewRoute(capacity: 1);
        route.SetGeometry([new GeoPoint(52.2, 21.0), new GeoPoint(52.3, 21.1)], 300.0, 5000.0);
        route.Activate(new GeoPoint(52.2, 21.0));

        var result = route.TryAddRide();

        Assert.True(result);
        Assert.Equal(1,                route.ActiveRideCount);
        Assert.Equal(RouteStatus.Full, route.Status);
    }

    [Fact]
    public void TryAddRide_WhenAlreadyFull_ReturnsFalseAndCountUnchanged()
    {
        var route = NewRoute(capacity: 1);
        route.SetGeometry([new GeoPoint(52.2, 21.0), new GeoPoint(52.3, 21.1)], 300.0, 5000.0);
        route.Activate(new GeoPoint(52.2, 21.0));
        route.TryAddRide(); // fills it

        var result = route.TryAddRide();

        Assert.False(result);
        Assert.Equal(1, route.ActiveRideCount);
    }

    // ─── RemoveRide ───────────────────────────────────────────────────────────

    [Fact]
    public void RemoveRide_DecrementsCount()
    {
        var route = NewRoute(capacity: 3);
        route.SetGeometry([new GeoPoint(52.2, 21.0), new GeoPoint(52.3, 21.1)], 300.0, 5000.0);
        route.Activate(new GeoPoint(52.2, 21.0));
        route.TryAddRide();
        route.TryAddRide();

        route.RemoveRide();

        Assert.Equal(1, route.ActiveRideCount);
    }

    [Fact]
    public void RemoveRide_WhenWasFull_ReturnsStatusToActive()
    {
        var route = NewRoute(capacity: 1);
        route.SetGeometry([new GeoPoint(52.2, 21.0), new GeoPoint(52.3, 21.1)], 300.0, 5000.0);
        route.Activate(new GeoPoint(52.2, 21.0));
        route.TryAddRide(); // → Full

        route.RemoveRide();

        Assert.Equal(RouteStatus.Active, route.Status);
        Assert.Equal(0, route.ActiveRideCount);
    }

    [Fact]
    public void RemoveRide_WhenCountIsZero_DoesNotGoNegative()
    {
        var route = NewRoute();

        route.RemoveRide();

        Assert.Equal(0, route.ActiveRideCount);
    }
}
