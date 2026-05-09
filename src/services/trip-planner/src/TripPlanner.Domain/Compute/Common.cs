namespace TripPlanner.Domain.Compute;

public record GeoPoint(double Latitude, double Longitude);

public record GeoLineString(IReadOnlyList<GeoPoint> Points);
