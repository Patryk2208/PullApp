using Npgsql;
using TripPlanner.Application.Repositories;
using TripPlanner.Domain.Compute;
using TripPlanner.Domain.Driver;

namespace TripPlanner.Infrastructure.Postgres;

public class PostgresDriverRouteRepository(DbSession db) : IDriverRouteRepository
{
    public async Task AddAsync(DriverRoute route, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = """
            INSERT INTO driver_routes
                (id, driver_id, status,
                 start_lat, start_lng, end_lat, end_lng,
                 route_geometry_json, eta_seconds, distance_meters,
                 job_id, created_at, activated_at, cancelled_at)
            VALUES
                (@id, @driver_id, @status,
                 @start_lat, @start_lng, @end_lat, @end_lng,
                 @route_geometry_json, @eta_seconds, @distance_meters,
                 @job_id, @created_at, @activated_at, @cancelled_at)
            """;
        BindParams(cmd, route);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<DriverRoute?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = "SELECT * FROM driver_routes WHERE id = @id";
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<DriverRoute?> GetActiveByDriverIdAsync(Guid driverId, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = """
            SELECT * FROM driver_routes
            WHERE driver_id = @driver_id AND status = 'Active'
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("driver_id", driverId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task UpdateAsync(DriverRoute route, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = """
            UPDATE driver_routes SET
                status               = @status,
                route_geometry_json  = @route_geometry_json,
                eta_seconds          = @eta_seconds,
                distance_meters      = @distance_meters,
                activated_at         = @activated_at,
                cancelled_at         = @cancelled_at
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("id",                   route.Id);
        cmd.Parameters.AddWithValue("status",               route.Status.ToString());
        cmd.Parameters.AddWithValue("route_geometry_json",  (object?)route.RouteGeometryJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("eta_seconds",          (object?)route.EtaSeconds        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("distance_meters",      (object?)route.DistanceMeters     ?? DBNull.Value);
        cmd.Parameters.AddWithValue("activated_at",         (object?)route.ActivatedAt        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("cancelled_at",         (object?)route.CancelledAt        ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> GetPendingRequestIdsForRouteAsync(
        Guid driverRouteId, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        // Finds ride_requests whose JSON match_results array contains the given driver_route_id.
        cmd.CommandText = """
            SELECT id FROM ride_requests
            WHERE status IN ('RoutesPresented', 'PendingDriver')
              AND match_results_json::jsonb @> @filter::jsonb
            """;
        cmd.Parameters.AddWithValue("filter", """[{{"driver_route_id":""" + '"' + driverRouteId + '"' + "}}]");

        var ids = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            ids.Add(reader.GetGuid(0));
        return ids;
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static void BindParams(NpgsqlCommand cmd, DriverRoute r)
    {
        cmd.Parameters.AddWithValue("id",                   r.Id);
        cmd.Parameters.AddWithValue("driver_id",            r.DriverId);
        cmd.Parameters.AddWithValue("status",               r.Status.ToString());
        cmd.Parameters.AddWithValue("start_lat",            r.StartPoint.Latitude);
        cmd.Parameters.AddWithValue("start_lng",            r.StartPoint.Longitude);
        cmd.Parameters.AddWithValue("end_lat",              r.EndPoint.Latitude);
        cmd.Parameters.AddWithValue("end_lng",              r.EndPoint.Longitude);
        cmd.Parameters.AddWithValue("route_geometry_json",  (object?)r.RouteGeometryJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("eta_seconds",          (object?)r.EtaSeconds        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("distance_meters",      (object?)r.DistanceMeters    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("job_id",               (object?)r.JobId             ?? DBNull.Value);
        cmd.Parameters.AddWithValue("created_at",           r.CreatedAt);
        cmd.Parameters.AddWithValue("activated_at",         (object?)r.ActivatedAt       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("cancelled_at",         (object?)r.CancelledAt       ?? DBNull.Value);
    }

    private static DriverRoute Map(NpgsqlDataReader r)
    {
        var route = EntityMapper.New<DriverRoute>();
        route.Set("Id",                 r.GetGuid(r.GetOrdinal("id")));
        route.Set("DriverId",           r.GetGuid(r.GetOrdinal("driver_id")));
        route.Set("StartPoint",         new GeoPoint(
                                            r.GetDouble(r.GetOrdinal("start_lat")),
                                            r.GetDouble(r.GetOrdinal("start_lng"))));
        route.Set("EndPoint",           new GeoPoint(
                                            r.GetDouble(r.GetOrdinal("end_lat")),
                                            r.GetDouble(r.GetOrdinal("end_lng"))));
        route.Set("JobId",              NullableGuid(r, "job_id"));
        route.Set("CreatedAt",          r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")));
        route.Set("Status",             Enum.Parse<DriverRouteStatus>(r.GetString(r.GetOrdinal("status"))));
        route.Set("RouteGeometryJson",  Nullable<string>(r, "route_geometry_json"));
        route.Set("EtaSeconds",         NullableInt(r, "eta_seconds"));
        route.Set("DistanceMeters",     NullableInt(r, "distance_meters"));
        route.Set("ActivatedAt",        NullableDto(r, "activated_at"));
        route.Set("CancelledAt",        NullableDto(r, "cancelled_at"));
        return route;
    }

    private static Guid? NullableGuid(NpgsqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetGuid(ord);
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

    private static T? Nullable<T>(NpgsqlDataReader r, string col) where T : class
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetFieldValue<T>(ord);
    }
}
