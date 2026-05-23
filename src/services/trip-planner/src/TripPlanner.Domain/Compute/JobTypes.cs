namespace TripPlanner.Domain.Compute;

// ─── Job payloads (Trip Planner → Route-Calc) ────────────────────────────────

public record DriverRouteJobPayload(GeoPoint Start, GeoPoint End);

public record MatchConstraints(double MaxDetourKm = 5, int MaxResults = 5);

public record PassengerMatchJobPayload(GeoPoint Start, GeoPoint End, MatchConstraints Constraints);

// ─── Job results (Route-Calc → Trip Planner) ─────────────────────────────────

public record DriverRouteJobResult(string RouteGeomJson, int EtaSeconds, int DistanceMeters);

public record MatchEntry(
    Guid DriverRouteId,
    Guid DriverId,
    int EtaToPassengerSeconds,
    int DetourMeters,
    double Score);

public record PassengerMatchJobResult(IReadOnlyList<MatchEntry> Matches);
