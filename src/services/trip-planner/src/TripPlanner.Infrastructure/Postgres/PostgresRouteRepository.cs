using System.Globalization;
using Npgsql;
using TripPlanner.Application.Repositories;
using TripPlanner.Domain.Compute;
using TripPlanner.Domain.Route;

namespace TripPlanner.Infrastructure.Postgres;

public class PostgresRouteRepository(DbSession db) : IRouteRepository
{
    private const string SelectCols = """
        id, driver_id, status,
        start_lat, start_lng, end_lat, end_lng,
        current_location_lat, current_location_lng,
        capacity, active_ride_count,
        ST_AsText(route_geom) AS route_geom_wkt,
        duration_seconds, distance_meters,
        created_at, activated_at
        """;

    public async Task AddAsync(Route route, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = $"""
            INSERT INTO routes
                (id, driver_id, status,
                 start_lat, start_lng, end_lat, end_lng,
                 current_location_lat, current_location_lng,
                 capacity, active_ride_count,
                 route_geom, duration_seconds, distance_meters,
                 created_at, activated_at)
            VALUES
                (@id, @driver_id, @status,
                 @start_lat, @start_lng, @end_lat, @end_lng,
                 @cur_lat, @cur_lng,
                 @capacity, @active_ride_count,
                 {GeomParam(route.RoutePoints)}, @duration_seconds, @distance_meters,
                 @created_at, @activated_at)
            """;
        BindAll(cmd, route);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Route?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = $"SELECT {SelectCols} FROM routes WHERE id = @id";
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    // Must be called inside an open transaction; serializes concurrent seat-count mutations.
    public async Task<Route?> GetByIdForUpdateAsync(Guid id, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = $"SELECT {SelectCols} FROM routes WHERE id = @id FOR UPDATE";
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<Route?> GetActiveByDriverIdAsync(Guid driverId, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = $"""
            SELECT {SelectCols} FROM routes
            WHERE driver_id = @driver_id
            ORDER BY created_at DESC
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("driver_id", driverId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task UpdateAsync(Route route, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = $"""
            UPDATE routes SET
                status               = @status,
                current_location_lat = @cur_lat,
                current_location_lng = @cur_lng,
                active_ride_count    = @active_ride_count,
                route_geom           = {GeomParam(route.RoutePoints)},
                duration_seconds     = @duration_seconds,
                distance_meters      = @distance_meters,
                activated_at         = @activated_at
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("id",               route.Id);
        cmd.Parameters.AddWithValue("status",           route.Status.ToString());
        cmd.Parameters.AddWithValue("cur_lat",          (object?)route.CurrentLocation?.Latitude  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("cur_lng",          (object?)route.CurrentLocation?.Longitude ?? DBNull.Value);
        cmd.Parameters.AddWithValue("active_ride_count", route.ActiveRideCount);
        BindGeomParams(cmd, route.RoutePoints);
        cmd.Parameters.AddWithValue("duration_seconds", (object?)route.DurationSeconds ?? DBNull.Value);
        cmd.Parameters.AddWithValue("distance_meters",  (object?)route.DistanceMeters  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("activated_at",     (object?)route.ActivatedAt     ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = "DELETE FROM routes WHERE id = @id";
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static void BindAll(NpgsqlCommand cmd, Route r)
    {
        cmd.Parameters.AddWithValue("id",               r.Id);
        cmd.Parameters.AddWithValue("driver_id",        r.DriverId);
        cmd.Parameters.AddWithValue("status",           r.Status.ToString());
        cmd.Parameters.AddWithValue("start_lat",        r.Start.Latitude);
        cmd.Parameters.AddWithValue("start_lng",        r.Start.Longitude);
        cmd.Parameters.AddWithValue("end_lat",          r.End.Latitude);
        cmd.Parameters.AddWithValue("end_lng",          r.End.Longitude);
        cmd.Parameters.AddWithValue("cur_lat",          (object?)r.CurrentLocation?.Latitude  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("cur_lng",          (object?)r.CurrentLocation?.Longitude ?? DBNull.Value);
        cmd.Parameters.AddWithValue("capacity",         r.Capacity);
        cmd.Parameters.AddWithValue("active_ride_count", r.ActiveRideCount);
        BindGeomParams(cmd, r.RoutePoints);
        cmd.Parameters.AddWithValue("duration_seconds", (object?)r.DurationSeconds ?? DBNull.Value);
        cmd.Parameters.AddWithValue("distance_meters",  (object?)r.DistanceMeters  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("created_at",       r.CreatedAt);
        cmd.Parameters.AddWithValue("activated_at",     (object?)r.ActivatedAt ?? DBNull.Value);
    }

    // Emits either ST_GeomFromText(@route_geom_wkt, 4326) or NULL literal.
    private static string GeomParam(IReadOnlyList<GeoPoint>? points) =>
        points is { Count: > 0 } ? "ST_GeomFromText(@route_geom_wkt, 4326)" : "NULL";

    // Binds @route_geom_wkt only when geometry is present.
    private static void BindGeomParams(NpgsqlCommand cmd, IReadOnlyList<GeoPoint>? points)
    {
        if (points is { Count: > 0 })
            cmd.Parameters.AddWithValue("route_geom_wkt", ToWkt(points));
    }

    private static string ToWkt(IReadOnlyList<GeoPoint> points)
    {
        var coords = string.Join(", ", points.Select(p =>
            $"{p.Longitude.ToString(CultureInfo.InvariantCulture)} {p.Latitude.ToString(CultureInfo.InvariantCulture)}"));
        return $"LINESTRING({coords})";
    }

    private static IReadOnlyList<GeoPoint> FromWkt(string wkt)
    {
        // "LINESTRING(lon lat, lon lat, ...)"
        var inner = wkt[11..^1];
        return inner.Split(',').Select(pair =>
        {
            var parts = pair.Trim().Split(' ');
            return new GeoPoint(
                double.Parse(parts[1], CultureInfo.InvariantCulture),
                double.Parse(parts[0], CultureInfo.InvariantCulture));
        }).ToList();
    }

    private static Route Map(NpgsqlDataReader r)
    {
        var route = EntityMapper.New<Route>();
        route.Set("Id",       r.GetGuid(r.GetOrdinal("id")));
        route.Set("DriverId", r.GetGuid(r.GetOrdinal("driver_id")));
        route.Set("Status",   Enum.Parse<RouteStatus>(r.GetString(r.GetOrdinal("status"))));
        route.Set("Start",    new GeoPoint(r.GetDouble(r.GetOrdinal("start_lat")), r.GetDouble(r.GetOrdinal("start_lng"))));
        route.Set("End",      new GeoPoint(r.GetDouble(r.GetOrdinal("end_lat")),   r.GetDouble(r.GetOrdinal("end_lng"))));
        route.Set("Capacity",        r.GetInt32(r.GetOrdinal("capacity")));
        route.Set("ActiveRideCount", r.GetInt32(r.GetOrdinal("active_ride_count")));
        route.Set("CreatedAt",       r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")));
        route.Set("ActivatedAt",     NullableDto(r, "activated_at"));

        var wktOrd = r.GetOrdinal("route_geom_wkt");
        if (!r.IsDBNull(wktOrd))
        {
            var points = FromWkt(r.GetString(wktOrd));
            route.Set("RoutePoints",     points);
            route.Set("DurationSeconds", NullableDouble(r, "duration_seconds"));
            route.Set("DistanceMeters",  NullableDouble(r, "distance_meters"));
        }

        var curLatOrd = r.GetOrdinal("current_location_lat");
        if (!r.IsDBNull(curLatOrd))
            route.Set("CurrentLocation", new GeoPoint(
                r.GetDouble(curLatOrd),
                r.GetDouble(r.GetOrdinal("current_location_lng"))));

        return route;
    }

    private static double? NullableDouble(NpgsqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetDouble(ord);
    }

    private static DateTimeOffset? NullableDto(NpgsqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetFieldValue<DateTimeOffset>(ord);
    }
}