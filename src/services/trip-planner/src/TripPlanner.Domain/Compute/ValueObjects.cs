namespace TripPlanner.Domain.Compute;

public record Point(double Latitude, double Longitude);

public record Route(Point[] Points);