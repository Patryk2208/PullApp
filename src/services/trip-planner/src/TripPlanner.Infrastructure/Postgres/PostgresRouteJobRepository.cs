using Npgsql;
using TripPlanner.Application.Repositories;
using TripPlanner.Infrastructure.Database;
using TripPlanner.Domain;
using TripPlanner.Domain.Compute;
using TripPlanner.Infrastructure.Postgres;

namespace TripPlanner.Infrastructure.Repositories;

public class PostgresRouteJobRepository(DbSession db) : IRouteJobRepository
{
    public async Task AddAsync(RouteJob job, CancellationToken ct)
    {
        var conn = await db.ConnAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO route_jobs
                (id, correlation_id, job_type, requester_id, status,
                 payload_json, result_json, error_reason,
                 created_at, completed_at, expires_at)
            VALUES
                (@id, @correlation_id, @job_type, @requester_id, @status,
                 @payload_json, @result_json, @error_reason,
                 @created_at, @completed_at, @expires_at)
            """;
        BindAll(cmd, job);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<RouteJob?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var conn = await db.ConnAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM route_jobs WHERE id = @id";
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<RouteJob?> GetByCorrelationIdAsync(Guid correlationId, CancellationToken ct)
    {
        var conn = await db.ConnAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM route_jobs WHERE correlation_id = @correlation_id LIMIT 1";
        cmd.Parameters.AddWithValue("correlation_id", correlationId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task UpdateAsync(RouteJob job, CancellationToken ct)
    {
        var conn = await db.ConnAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE route_jobs SET
                status       = @status,
                result_json  = @result_json,
                error_reason = @error_reason,
                completed_at = @completed_at
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("id",           job.Id);
        cmd.Parameters.AddWithValue("status",       job.Status.ToString());
        cmd.Parameters.AddWithValue("result_json",  (object?)job.ResultJson  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("error_reason", (object?)job.ErrorReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("completed_at", (object?)job.CompletedAt ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<RouteJob>> GetPendingOlderThanAsync(
        DateTimeOffset threshold, CancellationToken ct)
    {
        var conn = await db.ConnAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM route_jobs
            WHERE status = 'Pending' AND created_at < @threshold
            """;
        cmd.Parameters.AddWithValue("threshold", threshold);
        var list = new List<RouteJob>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(Map(reader));
        return list;
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static void BindAll(NpgsqlCommand cmd, RouteJob j)
    {
        cmd.Parameters.AddWithValue("id",             j.Id);
        cmd.Parameters.AddWithValue("correlation_id", j.CorrelationId);
        cmd.Parameters.AddWithValue("job_type",       j.JobType.ToString());
        cmd.Parameters.AddWithValue("requester_id",   j.RequesterId);
        cmd.Parameters.AddWithValue("status",         j.Status.ToString());
        cmd.Parameters.AddWithValue("payload_json",   j.PayloadJson);
        cmd.Parameters.AddWithValue("result_json",    (object?)j.ResultJson  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("error_reason",   (object?)j.ErrorReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("created_at",     j.CreatedAt);
        cmd.Parameters.AddWithValue("completed_at",   (object?)j.CompletedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("expires_at",     (object?)j.ExpiresAt   ?? DBNull.Value);
    }

    private static RouteJob Map(NpgsqlDataReader r)
    {
        var job = EntityMapper.New<RouteJob>();
        job.Set("Id",            r.GetGuid(r.GetOrdinal("id")));
        job.Set("CorrelationId", r.GetGuid(r.GetOrdinal("correlation_id")));
        job.Set("JobType",       Enum.Parse<JobType>(r.GetString(r.GetOrdinal("job_type"))));
        job.Set("RequesterId",   r.GetGuid(r.GetOrdinal("requester_id")));
        job.Set("PayloadJson",   r.GetString(r.GetOrdinal("payload_json")));
        job.Set("CreatedAt",     r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")));
        job.Set("ExpiresAt",     NullableDto(r, "expires_at"));
        job.Set("Status",        Enum.Parse<JobStatus>(r.GetString(r.GetOrdinal("status"))));
        job.Set("ResultJson",    NullableString(r, "result_json"));
        job.Set("ErrorReason",   NullableString(r, "error_reason"));
        job.Set("CompletedAt",   NullableDto(r, "completed_at"));
        return job;
    }

    private static DateTimeOffset? NullableDto(NpgsqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetFieldValue<DateTimeOffset>(ord);
    }

    private static string? NullableString(NpgsqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetString(ord);
    }
}
