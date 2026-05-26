using Microsoft.Extensions.Logging;
using Npgsql;
using TripPlanner.Application.Services;

namespace TripPlanner.Infrastructure.Postgres;

public class DatabaseInitializer(NpgsqlDataSource dataSource, ILogger<DatabaseInitializer> logger) : ISubscriber
{
    public async Task StartAsync(CancellationToken ct)
    {
        logger.LogInformation("Applying trip-planner database schema");
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = Schema;
        await cmd.ExecuteNonQueryAsync(ct);
        logger.LogInformation("Schema applied");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private const string Schema = """
        CREATE EXTENSION IF NOT EXISTS postgis;

        CREATE TABLE IF NOT EXISTS routes (
            id                   UUID          PRIMARY KEY,
            driver_id            UUID          NOT NULL,
            status               TEXT          NOT NULL,
            start_lat            FLOAT8        NOT NULL,
            start_lng            FLOAT8        NOT NULL,
            end_lat              FLOAT8        NOT NULL,
            end_lng              FLOAT8        NOT NULL,
            current_location_lat FLOAT8,
            current_location_lng FLOAT8,
            capacity             INTEGER       NOT NULL,
            active_ride_count    INTEGER       NOT NULL DEFAULT 0,
            geometry_json        TEXT,
            eta_seconds          INTEGER,
            distance_meters      INTEGER,
            created_at           TIMESTAMPTZ   NOT NULL,
            activated_at         TIMESTAMPTZ
        );

        CREATE INDEX IF NOT EXISTS idx_routes_driver_id ON routes(driver_id);
        CREATE INDEX IF NOT EXISTS idx_routes_status    ON routes(status);

        CREATE TABLE IF NOT EXISTS ride_requests (
            id              UUID        PRIMARY KEY,
            route_id        UUID        NOT NULL,
            passenger_id    UUID        NOT NULL,
            status          TEXT        NOT NULL,
            start_lat       FLOAT8      NOT NULL,
            start_lng       FLOAT8      NOT NULL,
            end_lat         FLOAT8      NOT NULL,
            end_lng         FLOAT8      NOT NULL,
            frozen_price_id UUID,
            created_at      TIMESTAMPTZ NOT NULL,
            rejected_at     TIMESTAMPTZ
        );

        CREATE INDEX IF NOT EXISTS idx_ride_requests_route_id     ON ride_requests(route_id);
        CREATE INDEX IF NOT EXISTS idx_ride_requests_passenger_id ON ride_requests(passenger_id);
        CREATE INDEX IF NOT EXISTS idx_ride_requests_status       ON ride_requests(status);

        CREATE TABLE IF NOT EXISTS rides (
            id                        UUID          PRIMARY KEY,
            route_id                  UUID          NOT NULL,
            driver_id                 UUID          NOT NULL,
            passenger_id              UUID          NOT NULL,
            status                    TEXT          NOT NULL,
            start_lat                 FLOAT8        NOT NULL,
            start_lng                 FLOAT8        NOT NULL,
            end_lat                   FLOAT8        NOT NULL,
            end_lng                   FLOAT8        NOT NULL,
            price                     NUMERIC(12,2) NOT NULL,
            cancellation_price        NUMERIC(12,2) NOT NULL,
            frozen_price_id           UUID,
            chat_room_id              UUID,
            driver_declared_pickup    BOOLEAN       NOT NULL DEFAULT false,
            passenger_declared_pickup BOOLEAN       NOT NULL DEFAULT false,
            passenger_declared_end    BOOLEAN       NOT NULL DEFAULT false,
            driver_declared_end       BOOLEAN       NOT NULL DEFAULT false,
            created_at                TIMESTAMPTZ   NOT NULL,
            started_at                TIMESTAMPTZ,
            ended_at                  TIMESTAMPTZ
        );

        CREATE INDEX IF NOT EXISTS idx_rides_route_id     ON rides(route_id);
        CREATE INDEX IF NOT EXISTS idx_rides_driver_id    ON rides(driver_id);
        CREATE INDEX IF NOT EXISTS idx_rides_passenger_id ON rides(passenger_id);
        CREATE INDEX IF NOT EXISTS idx_rides_status       ON rides(status);

        CREATE TABLE IF NOT EXISTS route_jobs (
            id             UUID        PRIMARY KEY,
            correlation_id UUID        NOT NULL,
            job_type       TEXT        NOT NULL,
            requester_id   UUID        NOT NULL,
            status         TEXT        NOT NULL,
            payload_json   TEXT        NOT NULL,
            result_json    TEXT,
            error_reason   TEXT,
            created_at     TIMESTAMPTZ NOT NULL,
            completed_at   TIMESTAMPTZ,
            expires_at     TIMESTAMPTZ
        );

        CREATE INDEX IF NOT EXISTS idx_route_jobs_correlation_id  ON route_jobs(correlation_id);
        CREATE INDEX IF NOT EXISTS idx_route_jobs_status_created  ON route_jobs(status, created_at);

        CREATE TABLE IF NOT EXISTS service_areas (
            id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            name       TEXT NOT NULL,
            area       geography(Polygon, 4326) NOT NULL,
            is_active  BOOLEAN NOT NULL DEFAULT true,
            created_at TIMESTAMPTZ NOT NULL DEFAULT now()
        );

        CREATE INDEX IF NOT EXISTS idx_service_areas_area ON service_areas USING GIST(area);
        """;
}
