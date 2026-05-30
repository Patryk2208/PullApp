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
                (id, route_id, driver_id, passenger_id, status,
                 start_lat, start_lng, end_lat, end_lng,
                 price, cancellation_price, frozen_price_id, chat_room_id,
                 driver_declared_pickup, passenger_declared_pickup,
                 passenger_declared_end, driver_declared_end,
                 created_at, started_at, ended_at)
            VALUES
                (@id, @route_id, @driver_id, @passenger_id, @status,
                 @start_lat, @start_lng, @end_lat, @end_lng,
                 @price, @cancellation_price, @frozen_price_id, @chat_room_id,
                 @driver_declared_pickup, @passenger_declared_pickup,
                 @passenger_declared_end, @driver_declared_end,
                 @created_at, @started_at, @ended_at)
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

    public async Task<IReadOnlyList<Ride>> GetActiveByRouteIdAsync(Guid routeId, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = """
            SELECT * FROM rides
            WHERE route_id = @route_id AND ended_at IS NULL
            """;
        cmd.Parameters.AddWithValue("route_id", routeId);
        var list = new List<Ride>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(Map(reader));
        return list;
    }

    public async Task UpdateAsync(Ride ride, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = """
            UPDATE rides SET
                status                    = @status,
                chat_room_id              = @chat_room_id,
                driver_declared_pickup    = @driver_declared_pickup,
                passenger_declared_pickup = @passenger_declared_pickup,
                passenger_declared_end    = @passenger_declared_end,
                driver_declared_end       = @driver_declared_end,
                started_at                = @started_at,
                ended_at                  = @ended_at
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("id",                        ride.Id);
        cmd.Parameters.AddWithValue("status",                    ride.Status.ToString());
        cmd.Parameters.AddWithValue("chat_room_id",              (object?)ride.ChatRoomId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("driver_declared_pickup",    ride.DriverDeclaredPickup);
        cmd.Parameters.AddWithValue("passenger_declared_pickup", ride.PassengerDeclaredPickup);
        cmd.Parameters.AddWithValue("passenger_declared_end",    ride.PassengerDeclaredEnd);
        cmd.Parameters.AddWithValue("driver_declared_end",       ride.DriverDeclaredEnd);
        cmd.Parameters.AddWithValue("started_at",                (object?)ride.StartedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ended_at",                  (object?)ride.EndedAt   ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteByRouteIdAsync(Guid routeId, CancellationToken ct)
    {
        await using var cmd = await db.CreateCommandAsync(ct);
        cmd.CommandText = "DELETE FROM rides WHERE route_id = @route_id";
        cmd.Parameters.AddWithValue("route_id", routeId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static void BindAll(NpgsqlCommand cmd, Ride ride)
    {
        cmd.Parameters.AddWithValue("id",                        ride.Id);
        cmd.Parameters.AddWithValue("route_id",                  ride.RouteId);
        cmd.Parameters.AddWithValue("driver_id",                 ride.DriverId);
        cmd.Parameters.AddWithValue("passenger_id",              ride.PassengerId);
        cmd.Parameters.AddWithValue("status",                    ride.Status.ToString());
        cmd.Parameters.AddWithValue("start_lat",                 ride.StartPoint.Latitude);
        cmd.Parameters.AddWithValue("start_lng",                 ride.StartPoint.Longitude);
        cmd.Parameters.AddWithValue("end_lat",                   ride.EndPoint.Latitude);
        cmd.Parameters.AddWithValue("end_lng",                   ride.EndPoint.Longitude);
        cmd.Parameters.AddWithValue("price",                     ride.Price);
        cmd.Parameters.AddWithValue("cancellation_price",        ride.CancellationPrice);
        cmd.Parameters.AddWithValue("frozen_price_id",           (object?)ride.FrozenPriceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("chat_room_id",              (object?)ride.ChatRoomId    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("driver_declared_pickup",    ride.DriverDeclaredPickup);
        cmd.Parameters.AddWithValue("passenger_declared_pickup", ride.PassengerDeclaredPickup);
        cmd.Parameters.AddWithValue("passenger_declared_end",    ride.PassengerDeclaredEnd);
        cmd.Parameters.AddWithValue("driver_declared_end",       ride.DriverDeclaredEnd);
        cmd.Parameters.AddWithValue("created_at",                ride.CreatedAt);
        cmd.Parameters.AddWithValue("started_at",                (object?)ride.StartedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ended_at",                  (object?)ride.EndedAt   ?? DBNull.Value);
    }

    private static Ride Map(NpgsqlDataReader r)
    {
        var ride = EntityMapper.New<Ride>();
        ride.Set("Id",          r.GetGuid(r.GetOrdinal("id")));
        ride.Set("RouteId",     r.GetGuid(r.GetOrdinal("route_id")));
        ride.Set("DriverId",    r.GetGuid(r.GetOrdinal("driver_id")));
        ride.Set("PassengerId", r.GetGuid(r.GetOrdinal("passenger_id")));
        ride.Set("Status",      Enum.Parse<RideStatus>(r.GetString(r.GetOrdinal("status"))));
        ride.Set("StartPoint",  new GeoPoint(r.GetDouble(r.GetOrdinal("start_lat")), r.GetDouble(r.GetOrdinal("start_lng"))));
        ride.Set("EndPoint",    new GeoPoint(r.GetDouble(r.GetOrdinal("end_lat")),   r.GetDouble(r.GetOrdinal("end_lng"))));
        ride.Set("Price",             r.GetDecimal(r.GetOrdinal("price")));
        ride.Set("CancellationPrice", r.GetDecimal(r.GetOrdinal("cancellation_price")));
        ride.Set("FrozenPriceId",     NullableGuid(r, "frozen_price_id"));
        ride.Set("ChatRoomId",        NullableGuid(r, "chat_room_id"));
        ride.Set("DriverDeclaredPickup",    r.GetBoolean(r.GetOrdinal("driver_declared_pickup")));
        ride.Set("PassengerDeclaredPickup", r.GetBoolean(r.GetOrdinal("passenger_declared_pickup")));
        ride.Set("PassengerDeclaredEnd",    r.GetBoolean(r.GetOrdinal("passenger_declared_end")));
        ride.Set("DriverDeclaredEnd",       r.GetBoolean(r.GetOrdinal("driver_declared_end")));
        ride.Set("CreatedAt",  r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")));
        ride.Set("StartedAt",  NullableDto(r, "started_at"));
        ride.Set("EndedAt",    NullableDto(r, "ended_at"));
        return ride;
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
