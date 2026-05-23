using TripPlanner.Domain.Compute;

namespace TripPlanner.Domain.Events;

public interface IEvent
{
    string EventType { get; }
}

// ─── Envelope ────────────────────────────────────────────────────────────────

public record Envelope<T>(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    T Payload);

// ─── Topics ──────────────────────────────────────────────────────────────────

public static class Topics
{
    public const string RideCompletions      = "ride-completions";
    public const string UserActions          = "user-actions";
    public const string NotificationTriggers = "notification-triggers";
}

// ─── ride-completions ─────────────────────────────────────────────────────────

public record RideCompletedEvent(
    Guid RideId,
    Guid DriverId,
    Guid PassengerId,
    Guid FrozenPriceId,
    decimal FrozenPriceAmount,
    int DistanceMeters,
    int DurationSeconds,
    DateTimeOffset CompletedAt) : IEvent
{
    public string EventType => "ride_completed";
}

public record RideCancelledEvent(
    Guid RideId,
    Guid DriverId,
    Guid PassengerId,
    Guid? FrozenPriceId,
    string CancelledBy,
    string CancellationPhase,
    DateTimeOffset CancelledAt) : IEvent
{
    public string EventType => "ride_cancelled";
}

public record RideInterruptedEvent(
    Guid RideId,
    Guid DriverId,
    Guid PassengerId,
    Guid? FrozenPriceId,
    DateTimeOffset InterruptedAt) : IEvent
{
    public string EventType => "ride_interrupted";
}

// ─── user-actions ─────────────────────────────────────────────────────────────

public record RouteSelectedEvent(
    Guid RequestId,
    Guid DriverId,
    Guid PassengerId,
    string PassengerDisplayName,
    GeoPoint PickupPoint,
    GeoPoint DropoffPoint,
    int EtaToPassengerSeconds,
    DateTimeOffset ExpiresAt) : IEvent
{
    public string EventType => "route_selected";
}

public record MatchConfirmedEvent(
    Guid RideId,
    Guid DriverId,
    Guid PassengerId) : IEvent
{
    public string EventType => "match_confirmed";
}

public record MatchDeclinedEvent(
    Guid RequestId,
    Guid DriverId,
    Guid PassengerId) : IEvent
{
    public string EventType => "match_declined";
}

public record DriverArrivedEvent(
    Guid RideId,
    Guid DriverId,
    Guid PassengerId) : IEvent
{
    public string EventType => "driver_arrived";
}

public record RideStartedEvent(
    Guid RideId,
    Guid DriverId,
    Guid PassengerId,
    DateTimeOffset StartedAt) : IEvent
{
    public string EventType => "ride_started";
}

// ─── notification-triggers ────────────────────────────────────────────────────

public record RatingPromptEvent(
    Guid RideId,
    Guid DriverId,
    Guid PassengerId) : IEvent
{
    public string EventType => "rating_prompt";
}
