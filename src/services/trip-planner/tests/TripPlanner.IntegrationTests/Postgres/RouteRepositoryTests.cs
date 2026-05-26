using TripPlanner.Domain.Compute;
using TripPlanner.Domain.Route;
using TripPlanner.Infrastructure.Postgres;
using TripPlanner.IntegrationTests.Fixtures;

namespace TripPlanner.IntegrationTests.Postgres;

[Collection("Postgres")]
public class RouteRepositoryTests(PostgresFixture db) : IAsyncLifetime
{
    private PostgresRouteRepository Repo() => new(db.NewSession());

    public Task InitializeAsync() => db.CleanAsync();
    public Task DisposeAsync()    => Task.CompletedTask;

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static Route NewRoute(int capacity = 3) =>
        Route.Create(Guid.NewGuid(), new GeoPoint(52.2, 21.0), new GeoPoint(52.3, 21.1), capacity);

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
        var route = NewRoute();
        var repo  = Repo();

        await repo.AddAsync(route, default);
        var loaded = await repo.GetByIdAsync(route.Id, default);

        Assert.NotNull(loaded);
        Assert.Equal(route.Id,                    loaded.Id);
        Assert.Equal(route.DriverId,              loaded.DriverId);
        Assert.Equal(RouteStatus.Calculating,     loaded.Status);
        Assert.Equal(route.Start.Latitude,        loaded.Start.Latitude, 6);
        Assert.Equal(route.Start.Longitude,       loaded.Start.Longitude, 6);
        Assert.Equal(route.End.Latitude,          loaded.End.Latitude, 6);
        Assert.Equal(route.End.Longitude,         loaded.End.Longitude, 6);
        Assert.Equal(route.Capacity,              loaded.Capacity);
        Assert.Equal(0,                           loaded.ActiveRideCount);
        Assert.Null(loaded.CurrentLocation);
        Assert.Null(loaded.GeometryJson);
        Assert.Null(loaded.EtaSeconds);
        Assert.Null(loaded.DistanceMeters);
        Assert.Null(loaded.ActivatedAt);
    }

    // ─── GetActiveByDriverId ──────────────────────────────────────────────────

    [Fact]
    public async Task GetActiveByDriverId_ReturnsNull_WhenNoneExist()
    {
        var result = await Repo().GetActiveByDriverIdAsync(Guid.NewGuid(), default);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetActiveByDriverId_ReturnsRoute_WhenExists()
    {
        var route = NewRoute();
        var repo  = Repo();

        await repo.AddAsync(route, default);
        var loaded = await repo.GetActiveByDriverIdAsync(route.DriverId, default);

        Assert.NotNull(loaded);
        Assert.Equal(route.Id, loaded.Id);
    }

    // ─── UpdateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_PersistsGeometryAndStatus()
    {
        var route = NewRoute();
        var repo  = Repo();

        await repo.AddAsync(route, default);
        route.SetGeometry("{}", etaSeconds: 300, distanceMeters: 5000);
        await repo.UpdateAsync(route, default);

        var loaded = await repo.GetByIdAsync(route.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal(RouteStatus.Created, loaded.Status);
        Assert.Equal("{}",  loaded.GeometryJson);
        Assert.Equal(300,   loaded.EtaSeconds);
        Assert.Equal(5000,  loaded.DistanceMeters);
    }

    [Fact]
    public async Task UpdateAsync_PersistsActivation()
    {
        var route    = NewRoute();
        var location = new GeoPoint(52.2, 21.0);
        var repo     = Repo();

        await repo.AddAsync(route, default);
        route.SetGeometry("{}", 300, 5000);
        route.Activate(location);
        await repo.UpdateAsync(route, default);

        var loaded = await repo.GetByIdAsync(route.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal(RouteStatus.Active,       loaded.Status);
        Assert.NotNull(loaded.CurrentLocation);
        Assert.Equal(location.Latitude,  loaded.CurrentLocation!.Latitude,  6);
        Assert.Equal(location.Longitude, loaded.CurrentLocation!.Longitude, 6);
        Assert.NotNull(loaded.ActivatedAt);
    }

    [Fact]
    public async Task UpdateAsync_PersistsActiveRideCount()
    {
        var route = NewRoute(capacity: 2);
        var repo  = Repo();

        await repo.AddAsync(route, default);
        route.SetGeometry("{}", 300, 5000);
        route.Activate(new GeoPoint(52.2, 21.0));
        route.TryAddRide();
        await repo.UpdateAsync(route, default);

        var loaded = await repo.GetByIdAsync(route.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal(1, loaded.ActiveRideCount);
        Assert.Equal(RouteStatus.Active, loaded.Status);
    }

    [Fact]
    public async Task TryAddRide_MarksRouteFullWhenCapacityReached()
    {
        var route = NewRoute(capacity: 1);
        var repo  = Repo();

        await repo.AddAsync(route, default);
        route.SetGeometry("{}", 300, 5000);
        route.Activate(new GeoPoint(52.2, 21.0));
        route.TryAddRide();
        await repo.UpdateAsync(route, default);

        var loaded = await repo.GetByIdAsync(route.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal(RouteStatus.Full, loaded.Status);
    }
}
