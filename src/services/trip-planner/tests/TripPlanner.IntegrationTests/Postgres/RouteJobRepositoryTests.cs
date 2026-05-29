using TripPlanner.Domain;
using TripPlanner.Domain.Compute;
using TripPlanner.Infrastructure.Postgres;
using TripPlanner.IntegrationTests.Fixtures;

namespace TripPlanner.IntegrationTests.Postgres;

[Collection("Postgres")]
public class RouteJobRepositoryTests(PostgresFixture db) : IAsyncLifetime
{
    private PostgresRouteJobRepository Repo() => new(db.NewSession());

    public Task InitializeAsync() => db.CleanAsync();
    public Task DisposeAsync()    => Task.CompletedTask;

    private static RouteJob NewJob(JobType type = JobType.DriverRoute) => new()
    {
        Id            = Guid.NewGuid(),
        CorrelationId = Guid.NewGuid(),
        JobType       = type,
        RequesterId   = Guid.NewGuid(),
        PayloadJson   = "{}",
        CreatedAt     = DateTimeOffset.UtcNow,
    };

    // ─── Add + GetById ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        var result = await Repo().GetByIdAsync(Guid.NewGuid(), default);
        Assert.Null(result);
    }

    [Fact]
    public async Task AddAndGetById_RoundTripsAllFields()
    {
        var job  = NewJob();
        var repo = Repo();

        await repo.AddAsync(job, default);
        var loaded = await repo.GetByIdAsync(job.Id, default);

        Assert.NotNull(loaded);
        Assert.Equal(job.Id,            loaded.Id);
        Assert.Equal(job.CorrelationId, loaded.CorrelationId);
        Assert.Equal(job.JobType,       loaded.JobType);
        Assert.Equal(job.RequesterId,   loaded.RequesterId);
        Assert.Equal(JobStatus.Pending, loaded.Status);
        Assert.Equal(job.PayloadJson,   loaded.PayloadJson);
        Assert.Null(loaded.ResultJson);
        Assert.Null(loaded.ErrorReason);
        Assert.Null(loaded.CompletedAt);
    }

    // ─── GetByCorrelationId ───────────────────────────────────────────────────

    [Fact]
    public async Task GetByCorrelationIdAsync_ReturnsNull_WhenNotFound()
    {
        var result = await Repo().GetByCorrelationIdAsync(Guid.NewGuid(), default);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByCorrelationId_ReturnsJob()
    {
        var job  = NewJob();
        var repo = Repo();

        await repo.AddAsync(job, default);
        var loaded = await repo.GetByCorrelationIdAsync(job.CorrelationId, default);

        Assert.NotNull(loaded);
        Assert.Equal(job.Id, loaded.Id);
    }

    // ─── UpdateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_PersistsCompletion()
    {
        var job  = NewJob();
        var repo = Repo();

        await repo.AddAsync(job, default);
        job.Complete("{\"result\":true}");
        await repo.UpdateAsync(job, default);

        var loaded = await repo.GetByIdAsync(job.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal(JobStatus.Completed,      loaded.Status);
        Assert.Equal("{\"result\":true}",      loaded.ResultJson);
        Assert.Null(loaded.ErrorReason);
        Assert.NotNull(loaded.CompletedAt);
    }

    [Fact]
    public async Task UpdateAsync_PersistsFailure()
    {
        var job  = NewJob();
        var repo = Repo();

        await repo.AddAsync(job, default);
        job.Fail("timeout");
        await repo.UpdateAsync(job, default);

        var loaded = await repo.GetByIdAsync(job.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal(JobStatus.Failed, loaded.Status);
        Assert.Equal("timeout",        loaded.ErrorReason);
        Assert.NotNull(loaded.CompletedAt);
    }

    [Fact]
    public async Task PassengerMatchJob_RoundTrips()
    {
        var job  = NewJob(JobType.PassengerMatch);
        var repo = Repo();

        await repo.AddAsync(job, default);
        var loaded = await repo.GetByIdAsync(job.Id, default);

        Assert.Equal(JobType.PassengerMatch, loaded!.JobType);
    }
}
