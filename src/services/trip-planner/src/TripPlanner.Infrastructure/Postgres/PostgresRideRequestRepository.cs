using Npgsql;
using TripPlanner.Application.Repositories;
using TripPlanner.Domain.Compute;
using TripPlanner.Domain.RideRequest;

namespace TripPlanner.Infrastructure.Postgres;

public class PostgresRideRequestRepository(DbSession db) : IRideRequestRepository
{
    public async Task AddAsync(RideRequest request, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = """
            INSERT INTO ride_requests
                (id, route_id, passenger_id, status,
                 start_lat, start_lng, end_lat, end_lng,
                 frozen_price_id, created_at, rejected_at)
            VALUES
                (@id, @route_id, @passenger_id, @status,
                 @start_lat, @start_lng, @end_lat, @end_lng,
                 @frozen_price_id, @created_at, @rejected_at)
            """;
        BindAll(cmd, request);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<RideRequest?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = "SELECT * FROM ride_requests WHERE id = @id";
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<RideRequest>> GetPendingByRouteIdAsync(Guid routeId, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = """
            SELECT * FROM ride_requests
            WHERE route_id = @route_id AND status = 'Pending'
            """;
        cmd.Parameters.AddWithValue("route_id", routeId);
        return await ReadListAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<RideRequest>> GetRejectedByRouteIdAsync(Guid routeId, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = """
            SELECT * FROM ride_requests
            WHERE route_id = @route_id AND status = 'Rejected'
            """;
        cmd.Parameters.AddWithValue("route_id", routeId);
        return await ReadListAsync(cmd, ct);
    }

    public async Task UpdateAsync(RideRequest request, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = """
            UPDATE ride_requests SET
                status          = @status,
                frozen_price_id = @frozen_price_id,
                rejected_at     = @rejected_at
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("id",             request.Id);
        cmd.Parameters.AddWithValue("status",         request.Status.ToString());
        cmd.Parameters.AddWithValue("frozen_price_id", (object?)request.FrozenPriceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("rejected_at",    (object?)request.RejectedAt     ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<RideRequest>> ReadListAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var list = new List<RideRequest>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(Map(reader));
        return list;
    }

    private static void BindAll(NpgsqlCommand cmd, RideRequest r)
    {
        cmd.Parameters.AddWithValue("id",             r.Id);
        cmd.Parameters.AddWithValue("route_id",       r.RouteId);
        cmd.Parameters.AddWithValue("passenger_id",   r.PassengerId);
        cmd.Parameters.AddWithValue("status",         r.Status.ToString());
        cmd.Parameters.AddWithValue("start_lat",      r.StartPoint.Latitude);
        cmd.Parameters.AddWithValue("start_lng",      r.StartPoint.Longitude);
        cmd.Parameters.AddWithValue("end_lat",        r.EndPoint.Latitude);
        cmd.Parameters.AddWithValue("end_lng",        r.EndPoint.Longitude);
        cmd.Parameters.AddWithValue("frozen_price_id", (object?)r.FrozenPriceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("created_at",     r.CreatedAt);
        cmd.Parameters.AddWithValue("rejected_at",    (object?)r.RejectedAt ?? DBNull.Value);
    }

    private static RideRequest Map(NpgsqlDataReader r)
    {
        var req = EntityMapper.New<RideRequest>();
        req.Set("Id",          r.GetGuid(r.GetOrdinal("id")));
        req.Set("RouteId",     r.GetGuid(r.GetOrdinal("route_id")));
        req.Set("PassengerId", r.GetGuid(r.GetOrdinal("passenger_id")));
        req.Set("Status",      Enum.Parse<RideRequestStatus>(r.GetString(r.GetOrdinal("status"))));
        req.Set("StartPoint",  new GeoPoint(r.GetDouble(r.GetOrdinal("start_lat")), r.GetDouble(r.GetOrdinal("start_lng"))));
        req.Set("EndPoint",    new GeoPoint(r.GetDouble(r.GetOrdinal("end_lat")),   r.GetDouble(r.GetOrdinal("end_lng"))));
        req.Set("FrozenPriceId", NullableGuid(r, "frozen_price_id"));
        req.Set("CreatedAt",   r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")));
        req.Set("RejectedAt",  NullableDto(r, "rejected_at"));
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
