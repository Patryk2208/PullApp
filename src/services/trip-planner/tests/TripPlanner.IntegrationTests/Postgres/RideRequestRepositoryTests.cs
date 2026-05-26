using TripPlanner.Domain.Compute;
using TripPlanner.Domain.RideRequest;
using TripPlanner.Infrastructure.Postgres;
using TripPlanner.IntegrationTests.Fixtures;

namespace TripPlanner.IntegrationTests.Postgres;

[Collection("Postgres")]
public class RideRequestRepositoryTests(PostgresFixture db) : IAsyncLifetime
{
    private PostgresRideRequestRepository Repo() => new(db.NewSession());

    public Task InitializeAsync() => db.CleanAsync();
    public Task DisposeAsync()    => Task.CompletedTask;

    private static readonly GeoPoint Start = new(52.2, 21.0);
    private static readonly GeoPoint End   = new(52.3, 21.1);

    private static RideRequest NewRequest(Guid? routeId = null) =>
        RideRequest.Create(routeId ?? Guid.NewGuid(), Guid.NewGuid(), Start, End);

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
        var req  = NewRequest();
        var repo = Repo();

        await repo.AddAsync(req, default);
        var loaded = await repo.GetByIdAsync(req.Id, default);

        Assert.NotNull(loaded);
        Assert.Equal(req.Id,          loaded.Id);
        Assert.Equal(req.RouteId,     loaded.RouteId);
        Assert.Equal(req.PassengerId, loaded.PassengerId);
        Assert.Equal(RideRequestStatus.Pending, loaded.Status);
        Assert.Equal(Start.Latitude,  loaded.StartPoint.Latitude,  6);
        Assert.Equal(Start.Longitude, loaded.StartPoint.Longitude, 6);
        Assert.Equal(End.Latitude,    loaded.EndPoint.Latitude,    6);
        Assert.Equal(End.Longitude,   loaded.EndPoint.Longitude,   6);
        Assert.Null(loaded.FrozenPriceId);
        Assert.Null(loaded.RejectedAt);
    }

    // ─── GetPendingByRouteId ──────────────────────────────────────────────────

    [Fact]
    public async Task GetPendingByRouteId_ReturnsEmpty_WhenNone()
    {
        var result = await Repo().GetPendingByRouteIdAsync(Guid.NewGuid(), default);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPendingByRouteId_ReturnsPending_ExcludesRejected()
    {
        var routeId  = Guid.NewGuid();
        var pending  = NewRequest(routeId);
        var rejected = NewRequest(routeId);
        rejected.Reject();

        var repo = Repo();
        await repo.AddAsync(pending,  default);
        await repo.AddAsync(rejected, default);

        var result = await repo.GetPendingByRouteIdAsync(routeId, default);

        Assert.Single(result);
        Assert.Equal(pending.Id, result[0].Id);
    }

    // ─── GetRejectedByRouteId ─────────────────────────────────────────────────

    [Fact]
    public async Task GetRejectedByRouteId_ReturnsRejected_ExcludesPending()
    {
        var routeId  = Guid.NewGuid();
        var pending  = NewRequest(routeId);
        var rejected = NewRequest(routeId);
        var repo     = Repo();

        await repo.AddAsync(pending,  default);
        await repo.AddAsync(rejected, default);
        rejected.Reject();
        await repo.UpdateAsync(rejected, default);

        var result = await repo.GetRejectedByRouteIdAsync(routeId, default);

        Assert.Single(result);
        Assert.Equal(rejected.Id, result[0].Id);
    }

    // ─── UpdateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_PersistsFrozenPriceId()
    {
        var req         = NewRequest();
        var frozenId    = Guid.NewGuid();
        var repo        = Repo();

        await repo.AddAsync(req, default);
        req.SetFrozenPrice(frozenId);
        await repo.UpdateAsync(req, default);

        var loaded = await repo.GetByIdAsync(req.Id, default);
        Assert.Equal(frozenId, loaded!.FrozenPriceId);
    }

    [Fact]
    public async Task UpdateAsync_PersistsRejectionWithTimestamp()
    {
        var req  = NewRequest();
        var repo = Repo();

        await repo.AddAsync(req, default);
        req.Reject();
        await repo.UpdateAsync(req, default);

        var loaded = await repo.GetByIdAsync(req.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal(RideRequestStatus.Rejected, loaded.Status);
        Assert.NotNull(loaded.RejectedAt);
    }

    [Fact]
    public async Task UpdateAsync_PersistsAccepted()
    {
        var req  = NewRequest();
        var repo = Repo();

        await repo.AddAsync(req, default);
        req.Accept();
        await repo.UpdateAsync(req, default);

        var loaded = await repo.GetByIdAsync(req.Id, default);
        Assert.Equal(RideRequestStatus.Accepted, loaded!.Status);
    }
}
