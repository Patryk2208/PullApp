namespace TripPlanner.UnitTests.Helpers;

/// <summary>
/// Factory helpers for building domain objects in specific states for tests.
/// </summary>
public static class Make
{
    public static readonly GeoPoint PointA = new(52.2, 21.0);
    public static readonly GeoPoint PointB = new(52.3, 21.1);

    // ─── Routes ──────────────────────────────────────────────────────────────

    public static Route CalculatingRoute(Guid? driverId = null, int capacity = 3) =>
        Route.Create(driverId ?? Guid.NewGuid(), PointA, PointB, capacity);

    public static Route CreatedRoute(Guid? driverId = null, int capacity = 3)
    {
        var r = CalculatingRoute(driverId, capacity);
        r.SetGeometry("{}", etaSeconds: 300, distanceMeters: 5000);
        return r;
    }

    public static Route ActiveRoute(Guid? driverId = null, int capacity = 3)
    {
        var r = CreatedRoute(driverId, capacity);
        r.Activate(PointA);
        return r;
    }

    // ─── Rides ───────────────────────────────────────────────────────────────

    public static Ride WaitingForDriverRide(Guid? routeId = null, Guid? driverId = null, Guid? passengerId = null) =>
        Ride.Create(
            routeId     ?? Guid.NewGuid(),
            driverId    ?? Guid.NewGuid(),
            passengerId ?? Guid.NewGuid(),
            PointA, PointB,
            price: 20m, cancellationPrice: 4m,
            frozenPriceId: Guid.NewGuid(),
            routeIsActive: true);

    public static Ride WaitingForActivationRide(Guid? routeId = null, Guid? driverId = null, Guid? passengerId = null) =>
        Ride.Create(
            routeId     ?? Guid.NewGuid(),
            driverId    ?? Guid.NewGuid(),
            passengerId ?? Guid.NewGuid(),
            PointA, PointB,
            price: 20m, cancellationPrice: 4m,
            frozenPriceId: Guid.NewGuid(),
            routeIsActive: false);

    public static Ride StartedRide(Guid? routeId = null, Guid? driverId = null, Guid? passengerId = null)
    {
        var ride = WaitingForDriverRide(routeId, driverId, passengerId);
        ride.DeclareDriverPickup();
        ride.DeclarePassengerPickup();
        return ride;
    }

    // ─── RideRequests ─────────────────────────────────────────────────────────

    public static RideRequest PendingRequest(Guid? routeId = null, Guid? passengerId = null)
    {
        var req = RideRequest.Create(routeId ?? Guid.NewGuid(), passengerId ?? Guid.NewGuid(), PointA, PointB);
        req.SetFrozenPrice(Guid.NewGuid(), price: 20m, cancellationPrice: 4m);
        return req;
    }

    // ─── RouteJobs ────────────────────────────────────────────────────────────

    public static RouteJob DriverRouteJob(Guid? requesterId = null, Guid? correlationId = null) => new()
    {
        Id            = Guid.NewGuid(),
        CorrelationId = correlationId ?? Guid.NewGuid(),
        JobType       = JobType.DriverRoute,
        RequesterId   = requesterId  ?? Guid.NewGuid(),
        PayloadJson   = "{}",
        CreatedAt     = DateTimeOffset.UtcNow,
    };

    public static RouteJob PassengerMatchJob(Guid? requesterId = null, Guid? correlationId = null) => new()
    {
        Id            = Guid.NewGuid(),
        CorrelationId = correlationId ?? Guid.NewGuid(),
        JobType       = JobType.PassengerMatch,
        RequesterId   = requesterId  ?? Guid.NewGuid(),
        PayloadJson   = "{}",
        CreatedAt     = DateTimeOffset.UtcNow,
    };

    // ─── Other ───────────────────────────────────────────────────────────────

    public static PriceQuote Quote(Guid? frozenId = null) => new(
        FrozenPriceId:    frozenId ?? Guid.NewGuid(),
        Price:            20m,
        CancellationPrice: 4m,
        Currency:         "PLN",
        ExpiresAt:        DateTimeOffset.UtcNow.AddMinutes(10));
}
