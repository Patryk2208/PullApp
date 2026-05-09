using System.Text.Json;
using Npgsql;
using TripPlanner.Application.Repositories;
using TripPlanner.Domain.Compute;
using TripPlanner.Domain.Passenger;

namespace TripPlanner.Infrastructure.Postgres;

public class PostgresRideRequestRepository(DbSession db) : IRideRequestRepository
{
    private static readonly string[] ActiveExcluded = ["Cancelled", "NoMatch"];

    public async Task AddAsync(RideRequest request, CancellationToken ct)
    {
        var conn = await db.ConnAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ride_requests
                (id, passenger_id, status,
                 start_lat, start_lng, end_lat, end_lng,
                 max_detour_km, max_results,
                 match_results_json, selected_route_id, job_id,
                 confirmation_deadline, created_at, updated_at)
            VALUES
                (@id, @passenger_id, @status,
                 @start_lat, @start_lng, @end_lat, @end_lng,
                 @max_detour_km, @max_results,
                 @match_results_json, @selected_route_id, @job_id,
                 @confirmation_deadline, @created_at, @updated_at)
            """;
        BindAll(cmd, request);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<RideRequest?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var conn = await db.ConnAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ride_requests WHERE id = @id";
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<RideRequest?> GetActiveByPassengerIdAsync(Guid passengerId, CancellationToken ct)
    {
        var conn = await db.ConnAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM ride_requests
            WHERE passenger_id = @passenger_id
              AND status NOT IN ('Cancelled', 'NoMatch')
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("passenger_id", passengerId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task UpdateAsync(RideRequest request, CancellationToken ct)
    {
        var conn = await db.ConnAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE ride_requests SET
                status                = @status,
                match_results_json    = @match_results_json,
                selected_route_id     = @selected_route_id,
                job_id                = @job_id,
                confirmation_deadline = @confirmation_deadline,
                updated_at            = @updated_at
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("id",                    request.Id);
        cmd.Parameters.AddWithValue("status",                request.Status.ToString());
        cmd.Parameters.AddWithValue("match_results_json",    SerializeMatches(request.MatchResults));
        cmd.Parameters.AddWithValue("selected_route_id",     (object?)request.SelectedRouteId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("job_id",                (object?)request.JobId           ?? DBNull.Value);
        cmd.Parameters.AddWithValue("confirmation_deadline", (object?)request.ConfirmationDeadline ?? DBNull.Value);
        cmd.Parameters.AddWithValue("updated_at",            request.UpdatedAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<RideRequest>> GetExpiredConfirmationsAsync(CancellationToken ct)
    {
        var conn = await db.ConnAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM ride_requests
            WHERE status = 'PendingDriver'
              AND confirmation_deadline < @now
            """;
        cmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
        return await ReadAll(cmd, ct);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static void BindAll(NpgsqlCommand cmd, RideRequest r)
    {
        cmd.Parameters.AddWithValue("id",                    r.Id);
        cmd.Parameters.AddWithValue("passenger_id",          r.PassengerId);
        cmd.Parameters.AddWithValue("status",                r.Status.ToString());
        cmd.Parameters.AddWithValue("start_lat",             r.StartPoint.Latitude);
        cmd.Parameters.AddWithValue("start_lng",             r.StartPoint.Longitude);
        cmd.Parameters.AddWithValue("end_lat",               r.EndPoint.Latitude);
        cmd.Parameters.AddWithValue("end_lng",               r.EndPoint.Longitude);
        cmd.Parameters.AddWithValue("max_detour_km",         r.Constraints.MaxDetourKm);
        cmd.Parameters.AddWithValue("max_results",           r.Constraints.MaxResults);
        cmd.Parameters.AddWithValue("match_results_json",    SerializeMatches(r.MatchResults));
        cmd.Parameters.AddWithValue("selected_route_id",     (object?)r.SelectedRouteId       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("job_id",                (object?)r.JobId                 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("confirmation_deadline", (object?)r.ConfirmationDeadline  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("created_at",            r.CreatedAt);
        cmd.Parameters.AddWithValue("updated_at",            r.UpdatedAt);
    }

    private static object SerializeMatches(IReadOnlyList<MatchEntry>? matches) =>
        matches is null ? DBNull.Value : JsonSerializer.Serialize(matches);

    private static IReadOnlyList<MatchEntry>? DeserializeMatches(NpgsqlDataReader r)
    {
        var ord = r.GetOrdinal("match_results_json");
        if (r.IsDBNull(ord)) return null;
        var json = r.GetString(ord);
        return JsonSerializer.Deserialize<List<MatchEntry>>(json);
    }

    private static async Task<IReadOnlyList<RideRequest>> ReadAll(NpgsqlCommand cmd, CancellationToken ct)
    {
        var list = new List<RideRequest>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(Map(reader));
        return list;
    }

    private static RideRequest Map(NpgsqlDataReader r)
    {
        var req = EntityMapper.New<RideRequest>();
        req.Set("Id",          r.GetGuid(r.GetOrdinal("id")));
        req.Set("PassengerId", r.GetGuid(r.GetOrdinal("passenger_id")));
        req.Set("StartPoint",  new GeoPoint(
                                   r.GetDouble(r.GetOrdinal("start_lat")),
                                   r.GetDouble(r.GetOrdinal("start_lng"))));
        req.Set("EndPoint",    new GeoPoint(
                                   r.GetDouble(r.GetOrdinal("end_lat")),
                                   r.GetDouble(r.GetOrdinal("end_lng"))));
        req.Set("Constraints", new MatchConstraints(
                                   r.GetDouble(r.GetOrdinal("max_detour_km")),
                                   r.GetInt32(r.GetOrdinal("max_results"))));
        req.Set("CreatedAt",   r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")));
        req.Set("Status",      Enum.Parse<RideRequestStatus>(r.GetString(r.GetOrdinal("status"))));
        req.Set("MatchResults",         DeserializeMatches(r));
        req.Set("SelectedRouteId",      NullableGuid(r, "selected_route_id"));
        req.Set("JobId",                NullableGuid(r, "job_id"));
        req.Set("ConfirmationDeadline", NullableDto(r, "confirmation_deadline"));
        req.Set("UpdatedAt",            r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("updated_at")));
        return req;
    }

    private static Guid? NullableGuid(NpgsqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetGuid(ord);
    }

    private static DateTimeOffset? NullableDto(NpgsqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetFieldValue<DateTimeOffset>(ord);
    }
}
