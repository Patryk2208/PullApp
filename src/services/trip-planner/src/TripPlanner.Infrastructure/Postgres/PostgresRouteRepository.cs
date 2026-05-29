using Npgsql;
using TripPlanner.Application.Repositories;
using TripPlanner.Domain.Compute;
using TripPlanner.Domain.Route;

namespace TripPlanner.Infrastructure.Postgres;

public class PostgresRouteRepository(DbSession db) : IRouteRepository
{
    public async Task AddAsync(Route route, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = """
            INSERT INTO routes
                (id, driver_id, status,
                 start_lat, start_lng, end_lat, end_lng,
                 current_location_lat, current_location_lng,
                 capacity, active_ride_count,
                 geometry_json, eta_seconds, distance_meters,
                 created_at, activated_at)
            VALUES
                (@id, @driver_id, @status,
                 @start_lat, @start_lng, @end_lat, @end_lng,
                 @cur_lat, @cur_lng,
                 @capacity, @active_ride_count,
                 @geometry_json, @eta_seconds, @distance_meters,
                 @created_at, @activated_at)
            """;
        BindAll(cmd, route);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Route?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = "SELECT * FROM routes WHERE id = @id";
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    // Must be called inside an open transaction; serializes concurrent seat-count mutations.
    public async Task<Route?> GetByIdForUpdateAsync(Guid id, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = "SELECT * FROM routes WHERE id = @id FOR UPDATE";
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<Route?> GetActiveByDriverIdAsync(Guid driverId, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = """
            SELECT * FROM routes
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
        cmd.CommandText = """
            UPDATE routes SET
                status               = @status,
                current_location_lat = @cur_lat,
                current_location_lng = @cur_lng,
                active_ride_count    = @active_ride_count,
                geometry_json        = @geometry_json,
                eta_seconds          = @eta_seconds,
                distance_meters      = @distance_meters,
                activated_at         = @activated_at
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("id",               route.Id);
        cmd.Parameters.AddWithValue("status",           route.Status.ToString());
        cmd.Parameters.AddWithValue("cur_lat",          (object?)route.CurrentLocation?.Latitude  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("cur_lng",          (object?)route.CurrentLocation?.Longitude ?? DBNull.Value);
        cmd.Parameters.AddWithValue("active_ride_count", route.ActiveRideCount);
        cmd.Parameters.AddWithValue("geometry_json",    (object?)route.GeometryJson   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("eta_seconds",      (object?)route.EtaSeconds     ?? DBNull.Value);
        cmd.Parameters.AddWithValue("distance_meters",  (object?)route.DistanceMeters ?? DBNull.Value);
        cmd.Parameters.AddWithValue("activated_at",     (object?)route.ActivatedAt    ?? DBNull.Value);
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
        cmd.Parameters.AddWithValue("geometry_json",    (object?)r.GeometryJson   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("eta_seconds",      (object?)r.EtaSeconds     ?? DBNull.Value);
        cmd.Parameters.AddWithValue("distance_meters",  (object?)r.DistanceMeters ?? DBNull.Value);
        cmd.Parameters.AddWithValue("created_at",       r.CreatedAt);
        cmd.Parameters.AddWithValue("activated_at",     (object?)r.ActivatedAt ?? DBNull.Value);
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
        route.Set("GeometryJson",    NullableString(r, "geometry_json"));
        route.Set("EtaSeconds",      NullableInt(r,    "eta_seconds"));
        route.Set("DistanceMeters",  NullableInt(r,    "distance_meters"));
        route.Set("CreatedAt",       r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")));
        route.Set("ActivatedAt",     NullableDto(r, "activated_at"));

        var curLatOrd = r.GetOrdinal("current_location_lat");
        if (!r.IsDBNull(curLatOrd))
            route.Set("CurrentLocation", new GeoPoint(
                r.GetDouble(curLatOrd),
                r.GetDouble(r.GetOrdinal("current_location_lng"))));

        return route;
    }

    private static string? NullableString(NpgsqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetString(ord);
    }

    private static int? NullableInt(NpgsqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetInt32(ord);
    }

    private static DateTimeOffset? NullableDto(NpgsqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetFieldValue<DateTimeOffset>(ord);
    }
}
