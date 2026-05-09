namespace TripPlanner.Application.Features.DTO.Sse;

// All events are sent on GET /api/passenger/route-request/{id}/stream
// and POST /api/passenger/ride/{rideId}/* endpoints.
// The `event:` field is the SSE event name; `data:` is the JSON-serialised record below.

// ─── Matching phase ───────────────────────────────────────────────────────────

// event: routes_ready
public record RoutesReadyEvent(
    Guid RequestId,
    IReadOnlyList<MatchOfferDto> Matches,
    DateTimeOffset ExpiresAt);

public record MatchOfferDto(
    Guid DriverRouteId,
    Guid DriverId,
    string DriverDisplayName,
    double DriverRating,
    int EtaToPassengerSeconds,
    int EtaToDestinationSeconds,
    int DetourMeters,
    double Score);

// event: awaiting_driver
public record AwaitingDriverEvent(
    Guid RequestId,
    DateTimeOffset ExpiresAt);

// event: match_confirmed
public record MatchConfirmedSseEvent(
    Guid RequestId,
    Guid RideId,
    Guid ChatRoomId,
    string DriverDisplayName,
    double DriverRating,
    int PickupEtaSeconds,
    decimal FrozenPrice,
    string Currency);

// event: match_declined
public record MatchDeclinedEvent(
    Guid RequestId,
    int RemainingOptions);

// event: match_timed_out
public record MatchTimedOutEvent(
    Guid RequestId,
    int RemainingOptions);

// event: no_match
public record NoMatchEvent(
    Guid RequestId,
    string Reason);   // "no_drivers_available" | "routing_timeout"

// event: cancelled  (request was cancelled before match)
public record RequestCancelledEvent(Guid RequestId);

// ─── Ride phase ───────────────────────────────────────────────────────────────

// event: driver_arrived
public record DriverArrivedSseEvent(Guid RideId);

// event: ride_started
public record RideStartedEvent(Guid RideId);

// event: ride_completed
public record RideCompletedSseEvent(
    Guid RideId,
    decimal AmountCharged,
    string Currency,
    string DriverDisplayName);

// event: ride_cancelled  (mid-ride cancellation)
public record RideCancelledSseEvent(
    Guid RideId,
    string CancelledBy,   // "driver" | "passenger" | "system"
    string? Reason);

// event: price_updated  (frozen price expired during pickup wait)
public record PriceUpdatedEvent(
    Guid RideId,
    decimal NewPrice,
    string Currency,
    bool MustReconfirm);
