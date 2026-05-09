namespace TripPlanner.Application.Features.DTO.Driver;

// 202 response for POST /api/driver/route and PUT /api/driver/route
public record RegisterRouteResponse(Guid JobId);

// 200 response for GET /api/driver/route/{jobId}
// status: "pending" | "completed" | "failed"
public record RouteJobStatusResponse(
    string Status,
    Guid? RouteId,
    object? RouteGeom,     // GeoJSON LineString; null when pending/failed
    int? EtaSeconds,
    int? DistanceMeters,
    string? Error);        // "routing_error" | "timeout"; null on success
