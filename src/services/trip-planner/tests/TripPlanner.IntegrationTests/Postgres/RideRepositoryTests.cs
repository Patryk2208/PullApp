using TripPlanner.Domain.Compute;
using TripPlanner.Domain.Ride;
using TripPlanner.Infrastructure.Postgres;
using TripPlanner.IntegrationTests.Fixtures;

namespace TripPlanner.IntegrationTests.Postgres;

[Collection("Postgres")]
public class RideRepositoryTests(PostgresFixture db) : IAsyncLifetime
{
    private PostgresRideRepository Repo() => new(db.NewSession());

    public Task InitializeAsync() => db.CleanAsync();
    public Task DisposeAsync()    => Task.CompletedTask;

    private static Ride NewRide(bool routeIsActive = true) =>
        Ride.Create(
            routeId:          Guid.NewGuid(),
            driverId:         Guid.NewGuid(),
            passengerId:      Guid.NewGuid(),
            startPoint:       new GeoPoint(52.2, 21.0),
            endPoint:         new GeoPoint(52.3, 21.1),
            price:            25.50m,
            cancellationPrice: 5.00m,
            frozenPriceId:    Guid.NewGuid(),
            routeIsActive:    routeIsActive);

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
        var ride = NewRide(routeIsActive: true);
        var repo = Repo();

        await repo.AddAsync(ride, default);
        var loaded = await repo.GetByIdAsync(ride.Id, default);

        Assert.NotNull(loaded);
        Assert.Equal(ride.Id,          loaded.Id);
        Assert.Equal(ride.RouteId,     loaded.RouteId);
        Assert.Equal(ride.DriverId,    loaded.DriverId);
        Assert.Equal(ride.PassengerId, loaded.PassengerId);
        Assert.Equal(RideStatus.WaitingForDriver, loaded.Status);
        Assert.Equal(ride.Price,             loaded.Price);
        Assert.Equal(ride.CancellationPrice, loaded.CancellationPrice);
        Assert.Equal(ride.FrozenPriceId,     loaded.FrozenPriceId);
        Assert.Null(loaded.ChatRoomId);
        Assert.False(loaded.DriverDeclaredPickup);
        Assert.False(loaded.PassengerDeclaredPickup);
        Assert.False(loaded.PassengerDeclaredEnd);
        Assert.False(loaded.DriverDeclaredEnd);
        Assert.Null(loaded.StartedAt);
        Assert.Null(loaded.EndedAt);
    }

    [Fact]
    public async Task AddAndGetById_WaitingForActivation_WhenRouteNotActive()
    {
        var ride = NewRide(routeIsActive: false);
        var repo = Repo();

        await repo.AddAsync(ride, default);
        var loaded = await repo.GetByIdAsync(ride.Id, default);

        Assert.Equal(RideStatus.WaitingForActivation, loaded!.Status);
    }

    // ─── GetActiveByRouteId ───────────────────────────────────────────────────

    [Fact]
    public async Task GetActiveByRouteId_ReturnsEmpty_WhenNone()
    {
        var result = await Repo().GetActiveByRouteIdAsync(Guid.NewGuid(), default);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetActiveByRouteId_ReturnsRide_WhenNotEnded()
    {
        var ride = NewRide();
        var repo = Repo();

        await repo.AddAsync(ride, default);
        var result = await repo.GetActiveByRouteIdAsync(ride.RouteId, default);

        Assert.Single(result);
        Assert.Equal(ride.Id, result[0].Id);
    }

    [Fact]
    public async Task GetActiveByRouteId_ExcludesEndedRides()
    {
        var ride = NewRide();
        var repo = Repo();

        await repo.AddAsync(ride, default);

        // Declare pickup by both
        ride.DeclareDriverPickup();
        ride.DeclarePassengerPickup();
        // Declare end by both
        ride.DeclarePassengerEnd();
        ride.DeclareDriverEnd();
        await repo.UpdateAsync(ride, default);

        var result = await repo.GetActiveByRouteIdAsync(ride.RouteId, default);
        Assert.Empty(result);
    }

    // ─── UpdateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_PersistsChatRoom()
    {
        var ride       = NewRide();
        var chatRoomId = Guid.NewGuid();
        var repo       = Repo();

        await repo.AddAsync(ride, default);
        ride.SetChatRoom(chatRoomId);
        await repo.UpdateAsync(ride, default);

        var loaded = await repo.GetByIdAsync(ride.Id, default);
        Assert.Equal(chatRoomId, loaded!.ChatRoomId);
    }

    [Fact]
    public async Task UpdateAsync_PersistsPickupDeclarationsAndStarted()
    {
        var ride = NewRide(routeIsActive: true);
        var repo = Repo();

        await repo.AddAsync(ride, default);
        ride.DeclareDriverPickup();
        ride.DeclarePassengerPickup();
        await repo.UpdateAsync(ride, default);

        var loaded = await repo.GetByIdAsync(ride.Id, default);
        Assert.NotNull(loaded);
        Assert.True(loaded.DriverDeclaredPickup);
        Assert.True(loaded.PassengerDeclaredPickup);
        Assert.Equal(RideStatus.Started, loaded.Status);
        Assert.NotNull(loaded.StartedAt);
    }

    [Fact]
    public async Task UpdateAsync_PersistsEndDeclarations()
    {
        var ride = NewRide(routeIsActive: true);
        var repo = Repo();

        await repo.AddAsync(ride, default);
        ride.DeclareDriverPickup();
        ride.DeclarePassengerPickup();
        ride.DeclarePassengerEnd();
        ride.DeclareDriverEnd();
        await repo.UpdateAsync(ride, default);

        var loaded = await repo.GetByIdAsync(ride.Id, default);
        Assert.NotNull(loaded);
        Assert.True(loaded.PassengerDeclaredEnd);
        Assert.True(loaded.DriverDeclaredEnd);
        Assert.NotNull(loaded.EndedAt);
    }
}
