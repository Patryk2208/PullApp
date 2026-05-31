namespace TripPlanner.Domain.Compute;

// ─── Job payloads (Trip Planner → Route-Calc) ────────────────────────────────

public record BestRouteJobPayload(GeoPoint Start, GeoPoint End, string CostType = "distance");

public record RideMatchingJobPayload(
    GeoPoint Start,
    GeoPoint End,
    long DepartureDate,
    int SeatsNeeded,
    int MaxDetourKm = 10,
    int TimeWindowMinutes = 120);

// ─── Job results (Route-Calc → Trip Planner) ─────────────────────────────────

public record BestRouteJobResult(
    IReadOnlyList<GeoPoint> Points,
    double DistanceMeters,
    double DurationSeconds);

public record MatchEntry(
    string RouteId,
    string DriverId,
    double MatchScore,
    double DetourKm,
    int PickupPointIndex,
    int DropoffPointIndex);

public record RideMatchingJobResult(IReadOnlyList<MatchEntry> Matches);