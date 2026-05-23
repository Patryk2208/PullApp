using Microsoft.Extensions.Logging;
using Npgsql;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;

namespace TripPlanner.Infrastructure.Postgres;

// Seeds a default Warsaw service area on first startup if the table is empty.
public class ServiceAreaSeeder(NpgsqlDataSource dataSource, ILogger<ServiceAreaSeeder> logger) : ISubscriber
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        await using var check = conn.CreateCommand();
        check.CommandText = "SELECT EXISTS (SELECT 1 FROM service_areas WHERE is_active = true)";
        var exists = (bool)(await check.ExecuteScalarAsync(ct))!;
        if (exists) return;

        logger.LogInformation("Seeding default Warsaw service area");

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO service_areas (name, area)
            VALUES (
                'Warsaw Metropolitan Area',
                ST_Buffer(
                    ST_SetSRID(ST_Point(21.0122, 52.2297), 4326)::geography,
                    25000
                )::geography
            )
            """;
        await cmd.ExecuteNonQueryAsync(ct);

        logger.LogInformation("Warsaw service area seeded");
    }
}
