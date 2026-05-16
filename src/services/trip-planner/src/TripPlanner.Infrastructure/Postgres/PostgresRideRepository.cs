using Npgsql;
using TripPlanner.Application.Repositories;
using TripPlanner.Domain.Compute;
using TripPlanner.Domain.Ride;

namespace TripPlanner.Infrastructure.Postgres;

public class PostgresRideRepository(DbSession db) : IRideRepository
{
    public async Task AddAsync(Ride ride, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = """
            INSERT INTO rides
                (id, request_id, driver_id, passenger_id, driver_route_id,
                 status,
                 frozen_price_id, frozen_price_amount, frozen_price_expires_at,
                 chat_room_id,
                 pickup_lat, pickup_lng,
                 dropoff_lat, dropoff_lng,
                 cancelled_by, cancellation_phase,
                 created_at, started_at, completed_at, cancelled_at)
            VALUES
                (@id, @request_id, @driver_id, @passenger_id, @driver_route_id,
                 @status,
                 @frozen_price_id, @frozen_price_amount, @frozen_price_expires_at,
                 @chat_room_id,
                 @pickup_lat, @pickup_lng,
                 @dropoff_lat, @dropoff_lng,
                 @cancelled_by, @cancellation_phase,
                 @created_at, @started_at, @completed_at, @cancelled_at)
            """;
        BindAll(cmd, ride);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Ride?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = "SELECT * FROM rides WHERE id = @id";
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<Ride?> GetActiveByDriverIdAsync(Guid driverId, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = """
            SELECT * FROM rides
            WHERE driver_id = @driver_id
              AND status NOT IN ('Completed', 'Cancelled')
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("driver_id", driverId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<Ride?> GetActiveByPassengerIdAsync(Guid passengerId, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = """
            SELECT * FROM rides
            WHERE passenger_id = @passenger_id
              AND status NOT IN ('Completed', 'Cancelled')
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("passenger_id", passengerId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task UpdateAsync(Ride ride, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = """
            UPDATE rides SET
                status                  = @status,
                frozen_price_id         = @frozen_price_id,
                frozen_price_amount     = @frozen_price_amount,
                frozen_price_expires_at = @frozen_price_expires_at,
                chat_room_id            = @chat_room_id,
                dropoff_lat             = @dropoff_lat,
                dropoff_lng             = @dropoff_lng,
                cancelled_by            = @cancelled_by,
                cancellation_phase      = @cancellation_phase,
                started_at              = @started_at,
                completed_at            = @completed_at,
                cancelled_at            = @cancelled_at
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("id",                      ride.Id);
        cmd.Parameters.AddWithValue("status",                  ride.Status.ToString());
        cmd.Parameters.AddWithValue("frozen_price_id",         (object?)ride.FrozenPriceId          ?? DBNull.Value);
        cmd.Parameters.AddWithValue("frozen_price_amount",     (object?)ride.FrozenPriceAmount       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("frozen_price_expires_at", (object?)ride.FrozenPriceExpiresAt    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("chat_room_id",            (object?)ride.ChatRoomId              ?? DBNull.Value);
        cmd.Parameters.AddWithValue("dropoff_lat",             (object?)ride.DropoffPoint?.Latitude  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("dropoff_lng",             (object?)ride.DropoffPoint?.Longitude ?? DBNull.Value);
        cmd.Parameters.AddWithValue("cancelled_by",            (object?)ride.CancelledByActor?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("cancellation_phase",      (object?)ride.Phase?.ToString()       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("started_at",              (object?)ride.StartedAt               ?? DBNull.Value);
        cmd.Parameters.AddWithValue("completed_at",            (object?)ride.CompletedAt             ?? DBNull.Value);
        cmd.Parameters.AddWithValue("cancelled_at",            (object?)ride.CancelledAt             ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<Ride>> GetRidesWithExpiringPriceFreezeAsync(
        DateTimeOffset threshold, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = """
            SELECT * FROM rides
            WHERE status IN ('Pickup', 'AwaitingPassenger')
              AND frozen_price_expires_at < @threshold
            """;
        cmd.Parameters.AddWithValue("threshold", threshold);
        var list = new List<Ride>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(Map(reader));
        return list;
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static void BindAll(NpgsqlCommand cmd, Ride r)
    {
        cmd.Parameters.AddWithValue("id",                      r.Id);
        cmd.Parameters.AddWithValue("request_id",              r.RequestId);
        cmd.Parameters.AddWithValue("driver_id",               r.DriverId);
        cmd.Parameters.AddWithValue("passenger_id",            r.PassengerId);
        cmd.Parameters.AddWithValue("driver_route_id",         r.DriverRouteId);
        cmd.Parameters.AddWithValue("status",                  r.Status.ToString());
        cmd.Parameters.AddWithValue("frozen_price_id",         (object?)r.FrozenPriceId          ?? DBNull.Value);
        cmd.Parameters.AddWithValue("frozen_price_amount",     (object?)r.FrozenPriceAmount       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("frozen_price_expires_at", (object?)r.FrozenPriceExpiresAt    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("chat_room_id",            (object?)r.ChatRoomId              ?? DBNull.Value);
        cmd.Parameters.AddWithValue("pickup_lat",              (object?)r.PickupPoint?.Latitude   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("pickup_lng",              (object?)r.PickupPoint?.Longitude  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("dropoff_lat",             (object?)r.DropoffPoint?.Latitude  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("dropoff_lng",             (object?)r.DropoffPoint?.Longitude ?? DBNull.Value);
        cmd.Parameters.AddWithValue("cancelled_by",            (object?)r.CancelledByActor?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("cancellation_phase",      (object?)r.Phase?.ToString()       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("created_at",              r.CreatedAt);
        cmd.Parameters.AddWithValue("started_at",              (object?)r.StartedAt               ?? DBNull.Value);
        cmd.Parameters.AddWithValue("completed_at",            (object?)r.CompletedAt             ?? DBNull.Value);
        cmd.Parameters.AddWithValue("cancelled_at",            (object?)r.CancelledAt             ?? DBNull.Value);
    }

    private static Ride Map(NpgsqlDataReader r)
    {
        var ride = EntityMapper.New<Ride>();
        ride.Set("Id",             r.GetGuid(r.GetOrdinal("id")));
        ride.Set("RequestId",      r.GetGuid(r.GetOrdinal("request_id")));
        ride.Set("DriverId",       r.GetGuid(r.GetOrdinal("driver_id")));
        ride.Set("PassengerId",    r.GetGuid(r.GetOrdinal("passenger_id")));
        ride.Set("DriverRouteId",  r.GetGuid(r.GetOrdinal("driver_route_id")));
        ride.Set("CreatedAt",      r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")));
        ride.Set("Status",         Enum.Parse<RideStatus>(r.GetString(r.GetOrdinal("status"))));
        ride.Set("FrozenPriceId",         NullableGuid(r, "frozen_price_id"));
        ride.Set("FrozenPriceAmount",     NullableDecimal(r, "frozen_price_amount"));
        ride.Set("FrozenPriceExpiresAt",  NullableDto(r, "frozen_price_expires_at"));
        ride.Set("ChatRoomId",            NullableGuid(r, "chat_room_id"));
        ride.Set("PickupPoint",           NullablePoint(r, "pickup_lat", "pickup_lng"));
        ride.Set("DropoffPoint",          NullablePoint(r, "dropoff_lat", "dropoff_lng"));
        ride.Set("CancelledByActor",      NullableEnum<CancelledBy>(r, "cancelled_by"));
        ride.Set("Phase",                 NullableEnum<CancellationPhase>(r, "cancellation_phase"));
        ride.Set("StartedAt",             NullableDto(r, "started_at"));
        ride.Set("CompletedAt",           NullableDto(r, "completed_at"));
        ride.Set("CancelledAt",           NullableDto(r, "cancelled_at"));
        return ride;
    }

    private static Guid? NullableGuid(NpgsqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetGuid(ord);
    }

    private static decimal? NullableDecimal(NpgsqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetDecimal(ord);
    }

    private static DateTimeOffset? NullableDto(NpgsqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetFieldValue<DateTimeOffset>(ord);
    }

    private static GeoPoint? NullablePoint(NpgsqlDataReader r, string latCol, string lngCol)
    {
        var latOrd = r.GetOrdinal(latCol);
        return r.IsDBNull(latOrd)
            ? null
            : new GeoPoint(r.GetDouble(latOrd), r.GetDouble(r.GetOrdinal(lngCol)));
    }

    private static TEnum? NullableEnum<TEnum>(NpgsqlDataReader r, string col) where TEnum : struct, Enum
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : Enum.Parse<TEnum>(r.GetString(ord));
    }
}
