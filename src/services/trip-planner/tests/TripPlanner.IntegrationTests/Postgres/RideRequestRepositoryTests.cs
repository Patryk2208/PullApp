using TripPlanner.Domain.Compute;
using TripPlanner.Domain.RideRequest;
using TripPlanner.Domain.Route;
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
        Assert.Equal(0m, loaded.Price);
        Assert.Equal(0m, loaded.CancellationPrice);
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
    public async Task UpdateAsync_PersistsFrozenPrice_WithPriceFields()
    {
        var req      = NewRequest();
        var frozenId = Guid.NewGuid();
        var repo     = Repo();

        await repo.AddAsync(req, default);
        req.SetFrozenPrice(frozenId, price: 18.50m, cancellationPrice: 3.75m);
        await repo.UpdateAsync(req, default);

        var loaded = await repo.GetByIdAsync(req.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal(frozenId,  loaded.FrozenPriceId);
        Assert.Equal(18.50m,    loaded.Price);
        Assert.Equal(3.75m,     loaded.CancellationPrice);
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

    // ─── GetByPassengerId (read-model) ────────────────────────────────────────

    [Fact]
    public async Task GetByPassengerId_ReturnsEmpty_WhenNone()
    {
        Assert.Empty(await Repo().GetByPassengerIdAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task GetByPassengerId_ReturnsOnlyThatPassengersRequests_NewestFirst()
    {
        var passenger = Guid.NewGuid();
        var repo      = Repo();
        var first  = RideRequest.Create(Guid.NewGuid(), passenger, Start, End);
        var second = RideRequest.Create(Guid.NewGuid(), passenger, Start, End);
        await repo.AddAsync(first,  default);
        await Task.Delay(5);
        await repo.AddAsync(second, default);
        await repo.AddAsync(RideRequest.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End), default); // inny pasażer

        var result = await repo.GetByPassengerIdAsync(passenger, default);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal(passenger, r.PassengerId));
        Assert.Equal(second.Id, result[0].Id); // newest first
    }

    // ─── GetPendingByDriverId (read-model, joins routes) ──────────────────────

    private async Task<Guid> SeedRouteAsync(Guid driverId)
    {
        var route = Route.Create(driverId, Start, End, capacity: 3);
        await new PostgresRouteRepository(db.NewSession()).AddAsync(route, default);
        return route.Id;
    }

    [Fact]
    public async Task GetPendingByDriverId_ReturnsPendingOnDriverRoutes_ExcludesRejectedAndOtherDrivers()
    {
        var driver   = Guid.NewGuid();
        var routeId  = await SeedRouteAsync(driver);
        var repo     = Repo();

        var pending  = RideRequest.Create(routeId, Guid.NewGuid(), Start, End);
        var rejected = RideRequest.Create(routeId, Guid.NewGuid(), Start, End);
        await repo.AddAsync(pending,  default);
        await repo.AddAsync(rejected, default);
        rejected.Reject();
        await repo.UpdateAsync(rejected, default);

        // pending request on a DIFFERENT driver's route → must be excluded
        var otherRouteId = await SeedRouteAsync(Guid.NewGuid());
        await repo.AddAsync(RideRequest.Create(otherRouteId, Guid.NewGuid(), Start, End), default);

        var result = await repo.GetPendingByDriverIdAsync(driver, default);

        Assert.Single(result);
        Assert.Equal(pending.Id, result[0].Id);
    }
}
