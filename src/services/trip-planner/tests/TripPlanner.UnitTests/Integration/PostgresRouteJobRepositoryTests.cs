using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TripPlanner.Domain;
using TripPlanner.Domain.Compute;
using TripPlanner.Infrastructure.Postgres;
using Xunit;

namespace TripPlanner.UnitTests.Integration;

// Integration tests for PostgresRouteJobRepository. These tests require a running Postgres
// instance with the appropriate schema.
//
// Main purpose is to verify that the repository correctly persists and retrieves RouteJob
// entities and that the mapping between the database and the domain model works as expected.
//
// It tests flows like: creating a job, updating its status to completed,
// retrieving by correlation ID, and querying for pending jobs older
// than a certain timestamp.
public class PostgresRouteJobRepositoryTests
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresRouteJobRepositoryTests()
    {
        var connectionString =
            "Host=localhost;Port=5433;Database=trip-planner;Username=pullapp;Password=ABCDEF";

        _dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
    }

    [Fact]
    public async Task AddAsync_ShouldPersistRouteJob()
    {
        // Arrange

        var initializer = new DatabaseInitializer(
            _dataSource,
            NullLogger<DatabaseInitializer>.Instance);

        await initializer.StartAsync(CancellationToken.None);

        var dbSession = new DbSession(_dataSource);

        var repository = new PostgresRouteJobRepository(dbSession);

        var job = new RouteJob
        {
            Id = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            JobType = JobType.DriverRoute,
            RequesterId = Guid.NewGuid(),
            PayloadJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act

        await repository.AddAsync(job, CancellationToken.None);

        var loaded = await repository.GetByIdAsync(job.Id, CancellationToken.None);

        // Assert

        Assert.NotNull(loaded);
        Assert.Equal(job.Id, loaded!.Id);
        Assert.Equal(JobStatus.Pending, loaded.Status);
        Assert.Equal(JobType.DriverRoute, loaded.JobType);
    }

    [Fact]
    public async Task UpdateAsync_ShouldPersistCompletedState()
    {
        // Arrange

        var initializer = new DatabaseInitializer(
            _dataSource,
            NullLogger<DatabaseInitializer>.Instance);

        await initializer.StartAsync(CancellationToken.None);

        var dbSession = new DbSession(_dataSource);

        var repository = new PostgresRouteJobRepository(dbSession);

        var job = new RouteJob
        {
            Id = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            JobType = JobType.DriverRoute,
            RequesterId = Guid.NewGuid(),
            PayloadJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow
        };

        await repository.AddAsync(job, CancellationToken.None);

        // Act

        var resultJson = """
        {
          "distanceMeters": 12000,
          "etaSeconds": 900
        }
        """;

        job.Complete(resultJson);

        await repository.UpdateAsync(job, CancellationToken.None);

        var loaded = await repository.GetByIdAsync(job.Id, CancellationToken.None);

        // Assert

        Assert.NotNull(loaded);

        Assert.Equal(JobStatus.Completed, loaded!.Status);

        Assert.Equal(resultJson, loaded.ResultJson);

        Assert.NotNull(loaded.CompletedAt);
    }

    [Fact]
    public async Task GetByCorrelationIdAsync_ShouldReturnCorrectJob()
    {
        // Arrange

        var initializer = new DatabaseInitializer(
            _dataSource,
            NullLogger<DatabaseInitializer>.Instance);

        await initializer.StartAsync(CancellationToken.None);

        var dbSession = new DbSession(_dataSource);

        var repository = new PostgresRouteJobRepository(dbSession);

        var correlationId = Guid.NewGuid();

        var job = new RouteJob
        {
            Id = Guid.NewGuid(),
            CorrelationId = correlationId,
            JobType = JobType.DriverRoute,
            RequesterId = Guid.NewGuid(),
            PayloadJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow
        };

        await repository.AddAsync(job, CancellationToken.None);

        // Act

        var loaded = await repository.GetByCorrelationIdAsync(
            correlationId,
            CancellationToken.None);

        // Assert

        Assert.NotNull(loaded);

        Assert.Equal(job.Id, loaded!.Id);

        Assert.Equal(correlationId, loaded.CorrelationId);
    }

    [Fact]
    public async Task GetPendingOlderThanAsync_ShouldReturnOnlyOldPendingJobs()
    {
        // Arrange

        var initializer = new DatabaseInitializer(
            _dataSource,
            NullLogger<DatabaseInitializer>.Instance);

        await initializer.StartAsync(CancellationToken.None);

        var dbSession = new DbSession(_dataSource);

        var repository = new PostgresRouteJobRepository(dbSession);

        var oldJob = new RouteJob
        {
            Id = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            JobType = JobType.DriverRoute,
            RequesterId = Guid.NewGuid(),
            PayloadJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
        };

        var freshJob = new RouteJob
        {
            Id = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            JobType = JobType.DriverRoute,
            RequesterId = Guid.NewGuid(),
            PayloadJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow
        };

        await repository.AddAsync(oldJob, CancellationToken.None);

        await repository.AddAsync(freshJob, CancellationToken.None);

        // Act

        var result = await repository.GetPendingOlderThanAsync(
            DateTimeOffset.UtcNow.AddHours(-1),
            CancellationToken.None);

        // Assert

        Assert.Contains(result, x => x.Id == oldJob.Id);

        Assert.DoesNotContain(result, x => x.Id == freshJob.Id);
    }
}