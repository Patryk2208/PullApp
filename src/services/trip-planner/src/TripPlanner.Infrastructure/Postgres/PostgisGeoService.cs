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
}
