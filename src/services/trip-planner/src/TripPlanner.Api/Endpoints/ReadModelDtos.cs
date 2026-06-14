using TripPlanner.Domain.Compute;
using TripPlanner.Domain.Ride;
using TripPlanner.Domain.RideRequest;

namespace TripPlanner.Api.Endpoints;

// Read-model DTOs for GET queries (passenger/driver views). Projected from the
// domain aggregates so the API surface doesn't leak internal types.

public record RideRequestDto(
    Guid RequestId,
    Guid RouteId,
    Guid PassengerId,
    string Status,
    GeoPointDto Start,
    GeoPointDto End,
    decimal Price,
    decimal CancellationPrice,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RejectedAt)
{
    public static RideRequestDto From(RideRequest r) =>
        new(r.Id, r.RouteId, r.PassengerId, r.Status.ToString(),
            GeoPointDto.From(r.StartPoint), GeoPointDto.From(r.EndPoint),
            r.Price, r.CancellationPrice, r.CreatedAt, r.RejectedAt);
}

public record RideDto(
    Guid RideId,
    Guid RouteId,
    Guid DriverId,
    Guid PassengerId,
    string Status,
    GeoPointDto Start,
    GeoPointDto End,
    decimal Price,
    decimal CancellationPrice,
    Guid? ChatRoomId,
    bool DriverDeclaredPickup,
    bool PassengerDeclaredPickup,
    bool PassengerDeclaredEnd,
    bool DriverDeclaredEnd,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt)
{
    public static RideDto From(Ride r) =>
        new(r.Id, r.RouteId, r.DriverId, r.PassengerId, r.Status.ToString(),
            GeoPointDto.From(r.StartPoint), GeoPointDto.From(r.EndPoint),
            r.Price, r.CancellationPrice, r.ChatRoomId,
            r.DriverDeclaredPickup, r.PassengerDeclaredPickup, r.PassengerDeclaredEnd, r.DriverDeclaredEnd,
            r.CreatedAt, r.StartedAt, r.EndedAt);
}

public record GeoPointDto(double Lat, double Lng)
{
    public static GeoPointDto From(GeoPoint p) => new(p.Latitude, p.Longitude);
}
