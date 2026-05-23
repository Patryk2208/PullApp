namespace TripPlanner.Application.Features.DTO.Passenger;

// POST /api/passenger/route-request
public record PassengerRouteRequest(
    GeoPointDto Start,
    GeoPointDto End,
    PassengerConstraintsDto? Constraints = null);

public record PassengerConstraintsDto(double MaxDetourKm = 5, int MaxResults = 5);

// POST /api/passenger/route-request/{requestId}/select
public record SelectRouteRequest(Guid DriverRouteId);

// POST /api/passenger/rides/{rideId}/start  (body-less, just POST)
public record PassengerStartRideRequest;

// POST /api/passenger/rides/{rideId}/confirm-price
public record ConfirmPriceRequest;
