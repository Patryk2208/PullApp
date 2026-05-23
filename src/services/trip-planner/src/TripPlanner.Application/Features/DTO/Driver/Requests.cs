namespace TripPlanner.Application.Features.DTO.Driver;

// POST /api/driver/route
public record RegisterRouteRequest(GeoPointDto Start, GeoPointDto End);

// PUT /api/driver/route
public record ModifyRouteRequest(GeoPointDto Start, GeoPointDto End);

// POST /api/driver/confirmation/{requestId}
public record DriverConfirmationRequest(bool Accepted);

// POST /api/driver/ride/{rideId}/complete
public record CompleteRideRequest(GeoPointDto DropoffPoint);

// POST /api/driver/rides/{rideId}/cancel  |  POST /api/passenger/rides/{rideId}/cancel
public record CancelRideRequest(string? Reason = null);
