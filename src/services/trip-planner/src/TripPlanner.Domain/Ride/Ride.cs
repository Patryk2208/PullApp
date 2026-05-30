using TripPlanner.Domain.Compute;

namespace TripPlanner.Domain.Ride;

public enum RideStatus { WaitingForActivation, WaitingForDriver, Started }

public class Ride
{
    public Guid Id { get; private set; }
    public Guid RouteId { get; private set; }
    public Guid DriverId { get; private set; }
    public Guid PassengerId { get; private set; }
    public GeoPoint StartPoint { get; private set; } = default!;
    public GeoPoint EndPoint { get; private set; } = default!;
    public decimal Price { get; private set; }
    public decimal CancellationPrice { get; private set; }
    public Guid? FrozenPriceId { get; private set; }
    public Guid? ChatRoomId { get; private set; }
    public RideStatus Status { get; private set; }

    // Pickup declarations (flow 7): driver must declare first; passenger declaration is ignored without it.
    public bool DriverDeclaredPickup { get; private set; }
    public bool PassengerDeclaredPickup { get; private set; }

    // End declarations (flow 8c): passenger must declare first; driver declaration is ignored without it.
    public bool PassengerDeclaredEnd { get; private set; }
    public bool DriverDeclaredEnd { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }

    private Ride() { }

    public static Ride Create(
        Guid routeId,
        Guid driverId,
        Guid passengerId,
        GeoPoint startPoint,
        GeoPoint endPoint,
        decimal price,
        decimal cancellationPrice,
        Guid frozenPriceId,
        bool routeIsActive) =>
        new()
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            DriverId = driverId,
            PassengerId = passengerId,
            StartPoint = startPoint,
            EndPoint = endPoint,
            Price = price,
            CancellationPrice = cancellationPrice,
            FrozenPriceId = frozenPriceId,
            Status = routeIsActive ? RideStatus.WaitingForDriver : RideStatus.WaitingForActivation,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    public void SetChatRoom(Guid chatRoomId) => ChatRoomId = chatRoomId;

    // Flow 1 propagation: when the route activates, waiting rides transition to WaitingForDriver.
    public void ActivateIfWaiting()
    {
        if (Status == RideStatus.WaitingForActivation)
            Status = RideStatus.WaitingForDriver;
    }

    // Flow 7: driver declares pickup.
    public bool DeclareDriverPickup()
    {
        if (Status != RideStatus.WaitingForDriver) return false;
        DriverDeclaredPickup = true;
        TryStart();
        return true;
    }

    // Flow 7: passenger declares pickup. Ignored if driver hasn't declared yet.
    public bool DeclarePassengerPickup()
    {
        if (!DriverDeclaredPickup) return false;
        PassengerDeclaredPickup = true;
        TryStart();
        return true;
    }

    private void TryStart()
    {
        if (!DriverDeclaredPickup || !PassengerDeclaredPickup) return;
        Status = RideStatus.Started;
        StartedAt = DateTimeOffset.UtcNow;
    }

    // Flow 8c: passenger declares end.
    public bool DeclarePassengerEnd()
    {
        if (Status != RideStatus.Started) return false;
        PassengerDeclaredEnd = true;
        TryEnd();
        return true;
    }

    // Flow 8c: driver declares end. Ignored if passenger hasn't declared yet.
    public bool DeclareDriverEnd()
    {
        if (!PassengerDeclaredEnd) return false;
        DriverDeclaredEnd = true;
        TryEnd();
        return true;
    }

    private void TryEnd()
    {
        if (!DriverDeclaredEnd || !PassengerDeclaredEnd) return;
        EndedAt = DateTimeOffset.UtcNow;
    }

    // Flow 8a/8b: passenger cancels before the ride starts.
    public void Cancel() => EndedAt = DateTimeOffset.UtcNow;

    public bool IsEnded => EndedAt.HasValue;
}
