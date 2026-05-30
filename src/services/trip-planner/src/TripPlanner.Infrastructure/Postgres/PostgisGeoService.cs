using TripPlanner.Application.Services;
using TripPlanner.Domain.Compute;

namespace TripPlanner.Infrastructure.Postgres;

public class PostgisGeoService(DbSession db) : IGeoService
{
    public async Task<bool> IsWithinServiceAreaAsync(GeoPoint point, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM service_areas
                WHERE is_active = true
                  AND ST_Covers(area, ST_SetSRID(ST_Point(@lng, @lat), 4326)::geography)
            )
            """;
        cmd.Parameters.AddWithValue("lng", point.Longitude);
        cmd.Parameters.AddWithValue("lat", point.Latitude);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<bool> IsNearAsync(GeoPoint a, GeoPoint b, double thresholdMeters, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = """
            SELECT ST_DWithin(
                ST_SetSRID(ST_Point(@lng_a, @lat_a), 4326)::geography,
                ST_SetSRID(ST_Point(@lng_b, @lat_b), 4326)::geography,
                @threshold
            )
            """;
        cmd.Parameters.AddWithValue("lng_a", a.Longitude);
        cmd.Parameters.AddWithValue("lat_a", a.Latitude);
        cmd.Parameters.AddWithValue("lng_b", b.Longitude);
        cmd.Parameters.AddWithValue("lat_b", b.Latitude);
        cmd.Parameters.AddWithValue("threshold", thresholdMeters);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }
}
