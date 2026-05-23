using TripPlanner.Domain.Compute;

namespace TripPlanner.Domain.Ride;

public enum RideStatus { Pickup, AwaitingPassenger, InRide, Completed, Cancelled }

public enum CancelledBy { Driver, Passenger, System }

public enum CancellationPhase { PrePickup, InRide }

// Created when a match is confirmed (driver accepted the passenger's request).
// This is the authoritative record of an active or completed ride.
public class Ride
{
    public Guid Id { get; init; }
    public Guid RequestId { get; init; }
    public Guid DriverId { get; init; }
    public Guid PassengerId { get; init; }
    public Guid DriverRouteId { get; init; }
    public RideStatus Status { get; private set; } = RideStatus.Pickup;

    // Set immediately after match_confirmed, before the ride record is persisted.
    public Guid? FrozenPriceId { get; private set; }
    public decimal? FrozenPriceAmount { get; private set; }
    public DateTimeOffset? FrozenPriceExpiresAt { get; private set; }

    // Set after Chat.CreateRoom gRPC call completes.
    public Guid? ChatRoomId { get; private set; }

    public GeoPoint? PickupPoint { get; init; }
    public GeoPoint? DropoffPoint { get; private set; }

    public CancelledBy? CancelledByActor { get; private set; }
    public CancellationPhase? Phase { get; private set; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public void SetChatRoom(Guid chatRoomId)
    {
        ChatRoomId = chatRoomId;
    }

    public void FreezePrice(Guid priceId, decimal amount, DateTimeOffset expiresAt)
    {
        FrozenPriceId = priceId;
        FrozenPriceAmount = amount;
        FrozenPriceExpiresAt = expiresAt;
    }

    public void UpdateFrozenPrice(Guid priceId, decimal amount, DateTimeOffset expiresAt)
    {
        FrozenPriceId = priceId;
        FrozenPriceAmount = amount;
        FrozenPriceExpiresAt = expiresAt;
    }

    public void MarkDriverArrived()
    {
        Status = RideStatus.AwaitingPassenger;
    }

    public void Start()
    {
        Status = RideStatus.InRide;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public void Complete(GeoPoint dropoffPoint)
    {
        Status = RideStatus.Completed;
        DropoffPoint = dropoffPoint;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel(CancelledBy by)
    {
        var phase = Status switch
        {
            RideStatus.Pickup or RideStatus.AwaitingPassenger => CancellationPhase.PrePickup,
            RideStatus.InRide => CancellationPhase.InRide,
            _ => throw new InvalidOperationException($"Cannot cancel ride in state {Status}")
        };

        Status = RideStatus.Cancelled;
        CancelledByActor = by;
        Phase = phase;
        CancelledAt = DateTimeOffset.UtcNow;
    }
}
