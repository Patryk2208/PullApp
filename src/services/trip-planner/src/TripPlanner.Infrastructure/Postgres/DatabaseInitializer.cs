using Microsoft.Extensions.Logging;
using Npgsql;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;

namespace TripPlanner.Infrastructure.Postgres;

// Runs on startup: creates the schema if it doesn't exist.
// Safe to run on every start (all statements use IF NOT EXISTS).
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

        CREATE TABLE IF NOT EXISTS driver_routes (
            id                  UUID        PRIMARY KEY,
            driver_id           UUID        NOT NULL,
            status              TEXT        NOT NULL,
            start_lat           FLOAT8      NOT NULL,
            start_lng           FLOAT8      NOT NULL,
            end_lat             FLOAT8      NOT NULL,
            end_lng             FLOAT8      NOT NULL,
            route_geometry_json TEXT,
            eta_seconds         INTEGER,
            distance_meters     INTEGER,
            job_id              UUID,
            created_at          TIMESTAMPTZ NOT NULL,
            activated_at        TIMESTAMPTZ,
            cancelled_at        TIMESTAMPTZ
        );

        CREATE INDEX IF NOT EXISTS idx_driver_routes_driver_id
            ON driver_routes(driver_id);
        CREATE INDEX IF NOT EXISTS idx_driver_routes_status
            ON driver_routes(status);

        CREATE TABLE IF NOT EXISTS ride_requests (
            id                   UUID        PRIMARY KEY,
            passenger_id         UUID        NOT NULL,
            status               TEXT        NOT NULL,
            start_lat            FLOAT8      NOT NULL,
            start_lng            FLOAT8      NOT NULL,
            end_lat              FLOAT8      NOT NULL,
            end_lng              FLOAT8      NOT NULL,
            max_detour_km        FLOAT8      NOT NULL,
            max_results          INTEGER     NOT NULL,
            match_results_json   TEXT,
            selected_route_id    UUID,
            job_id               UUID,
            confirmation_deadline TIMESTAMPTZ,
            created_at           TIMESTAMPTZ NOT NULL,
            updated_at           TIMESTAMPTZ NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_ride_requests_passenger_id
            ON ride_requests(passenger_id);
        CREATE INDEX IF NOT EXISTS idx_ride_requests_status
            ON ride_requests(status);

        CREATE TABLE IF NOT EXISTS rides (
            id                    UUID           PRIMARY KEY,
            request_id            UUID           NOT NULL,
            driver_id             UUID           NOT NULL,
            passenger_id          UUID           NOT NULL,
            driver_route_id       UUID           NOT NULL,
            status                TEXT           NOT NULL,
            frozen_price_id       UUID,
            frozen_price_amount   NUMERIC(12, 2),
            frozen_price_expires_at TIMESTAMPTZ,
            chat_room_id          UUID,
            pickup_lat            FLOAT8,
            pickup_lng            FLOAT8,
            dropoff_lat           FLOAT8,
            dropoff_lng           FLOAT8,
            cancelled_by          TEXT,
            cancellation_phase    TEXT,
            created_at            TIMESTAMPTZ    NOT NULL,
            started_at            TIMESTAMPTZ,
            completed_at          TIMESTAMPTZ,
            cancelled_at          TIMESTAMPTZ
        );

        CREATE INDEX IF NOT EXISTS idx_rides_driver_id
            ON rides(driver_id);
        CREATE INDEX IF NOT EXISTS idx_rides_passenger_id
            ON rides(passenger_id);
        CREATE INDEX IF NOT EXISTS idx_rides_status
            ON rides(status);

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

        CREATE INDEX IF NOT EXISTS idx_route_jobs_correlation_id
            ON route_jobs(correlation_id);
        CREATE INDEX IF NOT EXISTS idx_route_jobs_status_created
            ON route_jobs(status, created_at);

        CREATE TABLE IF NOT EXISTS service_areas (
            id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            name       TEXT NOT NULL,
            area       geography(Polygon, 4326) NOT NULL,
            is_active  BOOLEAN NOT NULL DEFAULT true,
            created_at TIMESTAMPTZ NOT NULL DEFAULT now()
        );

        CREATE INDEX IF NOT EXISTS idx_service_areas_area
            ON service_areas USING GIST(area);
        """;
}
