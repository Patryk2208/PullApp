using TripPlanner.Domain.Compute;

namespace TripPlanner.Domain.Events;

public interface IDomainEvent
{
    string EventType { get; }
}

public record Envelope<T>(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    T Payload);

public static class Topics
{
    public const string NotificationTriggers = "notification-triggers";
    public const string RideCompletions      = "ride-completions";
}

// ─── notification-triggers ────────────────────────────────────────────────────

// Flow 3: passenger created a request → notify driver.
public record RideRequestedEvent(
    Guid RequestId,
    Guid RouteId,
    Guid DriverId,
    Guid PassengerId,
    GeoPoint StartPoint,
    GeoPoint EndPoint) : IDomainEvent
{
    public string EventType => "ride_requested";
}

// Flow 4: driver rejected → notify passenger.
public record RideRejectedEvent(
    Guid RequestId,
    Guid RouteId,
    Guid DriverId,
    Guid PassengerId) : IDomainEvent
{
    public string EventType => "ride_rejected";
}

// Flow 5: driver accepted → notify passenger.
public record RideAcceptedEvent(
    Guid RideId,
    Guid RequestId,
    Guid RouteId,
    Guid DriverId,
    Guid PassengerId,
    Guid? ChatRoomId) : IDomainEvent
{
    public string EventType => "ride_accepted";
}

// Flow 1.5b: route deleted while passengers had pending rides → notify those passengers.
public record RouteDeletedEvent(
    Guid RouteId,
    Guid DriverId,
    IReadOnlyList<Guid> AffectedPassengerIds) : IDomainEvent
{
    public string EventType => "route_deleted";
}

// Flow 8: ride ended → notify passengers whose RideRequests were previously rejected.
public record RideEndedEvent(
    Guid RideId,
    Guid RouteId,
    Guid DriverId,
    Guid PassengerId,
    IReadOnlyList<Guid> NotifyPassengerIds) : IDomainEvent
{
    public string EventType => "ride_ended";
}

// ─── ride-completions ─────────────────────────────────────────────────────────

public record RideCompletedEvent(
    Guid RideId,
    Guid DriverId,
    Guid PassengerId,
    Guid FrozenPriceId,
    decimal Price,
    DateTimeOffset CompletedAt) : IDomainEvent
{
    public string EventType => "ride_completed";
}

public record RideCancelledEvent(
    Guid RideId,
    Guid DriverId,
    Guid PassengerId,
    Guid? FrozenPriceId,
    string CancelledBy,
    DateTimeOffset CancelledAt) : IDomainEvent
{
    public string EventType => "ride_cancelled";
}
