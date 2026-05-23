using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TripPlanner.Domain;
using TripPlanner.Domain.Compute;
using TripPlanner.Infrastructure.Postgres;
using Xunit;

namespace TripPlanner.UnitTests.Integration;

// Integration tests for PostgresRouteJobRepository. These tests require a running Postgres
// instance with the appropriate schema.
// Set TRIP_PLANNER_TEST_DB to override the connection string.
//
// Tests cover: persisting a job, updating its status to completed,
// retrieving by correlation ID, and querying for pending jobs older
// than a certain timestamp.
public class PostgresRouteJobRepositoryTests : IAsyncLifetime
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresRouteJobRepositoryTests()
    {
        var cs = Environment.GetEnvironmentVariable("TRIP_PLANNER_TEST_DB");
        _dataSource = new NpgsqlDataSourceBuilder(cs).Build();
    }

    public async Task InitializeAsync()
    {
        var initializer = new DatabaseInitializer(_dataSource, NullLogger<DatabaseInitializer>.Instance);
        await initializer.StartAsync(CancellationToken.None);
        await TruncateAsync();
    }

    public async Task DisposeAsync()
    {
        await TruncateAsync();
        await _dataSource.DisposeAsync();
    }

    [Fact]
    public async Task AddAsync_ShouldPersistRouteJob()
    {
        var repository = new PostgresRouteJobRepository(new DbSession(_dataSource));

        var job = new RouteJob
        {
            Id            = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            JobType       = JobType.DriverRoute,
            RequesterId   = Guid.NewGuid(),
            PayloadJson   = "{}",
            CreatedAt     = DateTimeOffset.UtcNow
        };

        await repository.AddAsync(job, CancellationToken.None);

        var loaded = await repository.GetByIdAsync(job.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(job.Id, loaded.Id);
        Assert.Equal(JobStatus.Pending, loaded.Status);
        Assert.Equal(JobType.DriverRoute, loaded.JobType);
    }

    [Fact]
    public async Task UpdateAsync_ShouldPersistCompletedState()
    {
        var repository = new PostgresRouteJobRepository(new DbSession(_dataSource));

        var job = new RouteJob
        {
            Id            = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            JobType       = JobType.DriverRoute,
            RequesterId   = Guid.NewGuid(),
            PayloadJson   = "{}",
            CreatedAt     = DateTimeOffset.UtcNow
        };

        await repository.AddAsync(job, CancellationToken.None);

        var resultJson = """
            {
              "distanceMeters": 12000,
              "etaSeconds": 900
            }
            """;

        job.Complete(resultJson);
        await repository.UpdateAsync(job, CancellationToken.None);

        var loaded = await repository.GetByIdAsync(job.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(JobStatus.Completed, loaded.Status);
        Assert.Equal(resultJson, loaded.ResultJson);
        Assert.NotNull(loaded.CompletedAt);
    }

    [Fact]
    public async Task GetByCorrelationIdAsync_ShouldReturnCorrectJob()
    {
        var repository    = new PostgresRouteJobRepository(new DbSession(_dataSource));
        var correlationId = Guid.NewGuid();

        var job = new RouteJob
        {
            Id            = Guid.NewGuid(),
            CorrelationId = correlationId,
            JobType       = JobType.DriverRoute,
            RequesterId   = Guid.NewGuid(),
            PayloadJson   = "{}",
            CreatedAt     = DateTimeOffset.UtcNow
        };

        await repository.AddAsync(job, CancellationToken.None);

        var loaded = await repository.GetByCorrelationIdAsync(correlationId, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(job.Id, loaded.Id);
        Assert.Equal(correlationId, loaded.CorrelationId);
    }

    [Fact]
    public async Task GetPendingOlderThanAsync_ShouldReturnOnlyOldPendingJobs()
    {
        var repository = new PostgresRouteJobRepository(new DbSession(_dataSource));

        var oldJob = new RouteJob
        {
            Id            = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            JobType       = JobType.DriverRoute,
            RequesterId   = Guid.NewGuid(),
            PayloadJson   = "{}",
            CreatedAt     = DateTimeOffset.UtcNow.AddHours(-2)
        };

        var freshJob = new RouteJob
        {
            Id            = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            JobType       = JobType.DriverRoute,
            RequesterId   = Guid.NewGuid(),
            PayloadJson   = "{}",
            CreatedAt     = DateTimeOffset.UtcNow
        };

        await repository.AddAsync(oldJob, CancellationToken.None);
        await repository.AddAsync(freshJob, CancellationToken.None);

        var result = await repository.GetPendingOlderThanAsync(
            DateTimeOffset.UtcNow.AddHours(-1),
            CancellationToken.None);

        Assert.Contains(result, x => x.Id == oldJob.Id);
        Assert.DoesNotContain(result, x => x.Id == freshJob.Id);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private async Task TruncateAsync()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE driver_routes, ride_requests, rides, route_jobs CASCADE";
        await cmd.ExecuteNonQueryAsync();
    }
}