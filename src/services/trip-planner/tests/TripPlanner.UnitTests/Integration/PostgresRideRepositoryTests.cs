using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TripPlanner.Domain.Compute;
using TripPlanner.Domain.Ride;
using TripPlanner.Infrastructure.Postgres;
using Xunit;

namespace TripPlanner.UnitTests.Integration;

// Integration tests for PostgresRideRepository.
// Requires a running Postgres instance with the trip-planner schema.
// Set TRIP_PLANNER_TEST_DB to override the connection string.
//
// Tests cover: persisting a new ride, retrieving it by ID, querying active rides
// by driver and passenger, updating through status transitions, and finding rides
// with expiring price freezes.
public class PostgresRideRepositoryTests : IAsyncLifetime
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresRideRepositoryTests()
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

    // ─── AddAsync / GetByIdAsync ──────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_ShouldPersistRide()
    {
        var repo = new PostgresRideRepository(new DbSession(_dataSource));

        var ride = BuildRide();
        await repo.AddAsync(ride, CancellationToken.None);

        var loaded = await repo.GetByIdAsync(ride.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(ride.Id, loaded!.Id);
        Assert.Equal(ride.RequestId, loaded.RequestId);
        Assert.Equal(ride.DriverId, loaded.DriverId);
        Assert.Equal(ride.PassengerId, loaded.PassengerId);
        Assert.Equal(ride.DriverRouteId, loaded.DriverRouteId);
        Assert.Equal(RideStatus.Pickup, loaded.Status);
        Assert.Equal(ride.PickupPoint, loaded.PickupPoint);
        Assert.Null(loaded.DropoffPoint);
        Assert.Null(loaded.StartedAt);
        Assert.Null(loaded.CompletedAt);
        Assert.Null(loaded.CancelledAt);
    }

    // ─── GetActiveByDriverIdAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetActiveByDriverIdAsync_ShouldReturnActiveRide()
    {
        var repo     = new PostgresRideRepository(new DbSession(_dataSource));
        var driverId = Guid.NewGuid();

        var ride = BuildRide(driverId: driverId);
        await repo.AddAsync(ride, CancellationToken.None);

        var found = await repo.GetActiveByDriverIdAsync(driverId, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(ride.Id, found!.Id);
    }

    [Fact]
    public async Task GetActiveByDriverIdAsync_ShouldReturnNull_WhenRideCompleted()
    {
        var repo     = new PostgresRideRepository(new DbSession(_dataSource));
        var driverId = Guid.NewGuid();

        var ride = BuildRide(driverId: driverId);
        await repo.AddAsync(ride, CancellationToken.None);

        ride.Start();
        ride.Complete(new GeoPoint(52.26, 21.04));
        await repo.UpdateAsync(ride, CancellationToken.None);

        var found = await repo.GetActiveByDriverIdAsync(driverId, CancellationToken.None);

        Assert.Null(found);
    }

    // ─── GetActiveByPassengerIdAsync ──────────────────────────────────────────

    [Fact]
    public async Task GetActiveByPassengerIdAsync_ShouldReturnActiveRide()
    {
        var repo        = new PostgresRideRepository(new DbSession(_dataSource));
        var passengerId = Guid.NewGuid();

        var ride = BuildRide(passengerId: passengerId);
        await repo.AddAsync(ride, CancellationToken.None);

        var found = await repo.GetActiveByPassengerIdAsync(passengerId, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(ride.Id, found!.Id);
    }

    [Fact]
    public async Task GetActiveByPassengerIdAsync_ShouldReturnNull_WhenRideCancelled()
    {
        var repo        = new PostgresRideRepository(new DbSession(_dataSource));
        var passengerId = Guid.NewGuid();

        var ride = BuildRide(passengerId: passengerId);
        await repo.AddAsync(ride, CancellationToken.None);

        ride.Cancel(CancelledBy.Passenger);
        await repo.UpdateAsync(ride, CancellationToken.None);

        var found = await repo.GetActiveByPassengerIdAsync(passengerId, CancellationToken.None);

        Assert.Null(found);
    }

    // ─── UpdateAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ShouldPersistStartedState()
    {
        var repo = new PostgresRideRepository(new DbSession(_dataSource));

        var ride = BuildRide();
        await repo.AddAsync(ride, CancellationToken.None);

        ride.Start();
        await repo.UpdateAsync(ride, CancellationToken.None);

        var loaded = await repo.GetByIdAsync(ride.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(RideStatus.InRide, loaded!.Status);
        Assert.NotNull(loaded.StartedAt);
        Assert.Null(loaded.CompletedAt);
    }

    [Fact]
    public async Task UpdateAsync_ShouldPersistCompletedState()
    {
        var repo = new PostgresRideRepository(new DbSession(_dataSource));

        var ride = BuildRide();
        await repo.AddAsync(ride, CancellationToken.None);

        ride.Start();
        var dropoff = new GeoPoint(52.26, 21.04);
        ride.Complete(dropoff);
        await repo.UpdateAsync(ride, CancellationToken.None);

        var loaded = await repo.GetByIdAsync(ride.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(RideStatus.Completed, loaded!.Status);
        Assert.Equal(dropoff, loaded.DropoffPoint);
        Assert.NotNull(loaded.CompletedAt);
        Assert.Null(loaded.CancelledAt);
    }

    [Fact]
    public async Task UpdateAsync_ShouldPersistCancelledState()
    {
        var repo = new PostgresRideRepository(new DbSession(_dataSource));

        var ride = BuildRide();
        await repo.AddAsync(ride, CancellationToken.None);

        ride.Cancel(CancelledBy.Driver);
        await repo.UpdateAsync(ride, CancellationToken.None);

        var loaded = await repo.GetByIdAsync(ride.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(RideStatus.Cancelled, loaded!.Status);
        Assert.Equal(CancelledBy.Driver, loaded.CancelledByActor);
        Assert.Equal(CancellationPhase.PrePickup, loaded.Phase);
        Assert.NotNull(loaded.CancelledAt);
    }

    [Fact]
    public async Task UpdateAsync_ShouldPersistFrozenPrice()
    {
        var repo = new PostgresRideRepository(new DbSession(_dataSource));

        var ride = BuildRide();
        await repo.AddAsync(ride, CancellationToken.None);

        var priceId   = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        ride.FreezePrice(priceId, 24.99m, expiresAt);
        await repo.UpdateAsync(ride, CancellationToken.None);

        var loaded = await repo.GetByIdAsync(ride.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(priceId, loaded!.FrozenPriceId);
        Assert.Equal(24.99m, loaded.FrozenPriceAmount);
        Assert.NotNull(loaded.FrozenPriceExpiresAt);
    }

    // ─── GetRidesWithExpiringPriceFreezeAsync ─────────────────────────────────

    [Fact]
    public async Task GetRidesWithExpiringPriceFreezeAsync_ShouldReturnOnlyExpiringRides()
    {
        var repo = new PostgresRideRepository(new DbSession(_dataSource));

        var expiringRide = BuildRide();
        expiringRide.FreezePrice(Guid.NewGuid(), 19.99m, DateTimeOffset.UtcNow.AddHours(-1));
        await repo.AddAsync(expiringRide, CancellationToken.None);
        await repo.UpdateAsync(expiringRide, CancellationToken.None);

        var freshRide = BuildRide();
        freshRide.FreezePrice(Guid.NewGuid(), 19.99m, DateTimeOffset.UtcNow.AddHours(1));
        await repo.AddAsync(freshRide, CancellationToken.None);
        await repo.UpdateAsync(freshRide, CancellationToken.None);

        var noPriceRide = BuildRide();
        await repo.AddAsync(noPriceRide, CancellationToken.None);

        var threshold = DateTimeOffset.UtcNow;
        var result = await repo.GetRidesWithExpiringPriceFreezeAsync(threshold, CancellationToken.None);

        Assert.Contains(result, r => r.Id == expiringRide.Id);
        Assert.DoesNotContain(result, r => r.Id == freshRide.Id);
        Assert.DoesNotContain(result, r => r.Id == noPriceRide.Id);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static Ride BuildRide(Guid? driverId = null, Guid? passengerId = null) => new()
    {
        Id            = Guid.NewGuid(),
        RequestId     = Guid.NewGuid(),
        DriverId      = driverId   ?? Guid.NewGuid(),
        PassengerId   = passengerId ?? Guid.NewGuid(),
        DriverRouteId = Guid.NewGuid(),
        PickupPoint   = new GeoPoint(52.2297, 21.0122),
        CreatedAt     = DateTimeOffset.UtcNow,
    };

    private async Task TruncateAsync()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE driver_routes, ride_requests, rides, route_jobs CASCADE";
        await cmd.ExecuteNonQueryAsync();
    }
}