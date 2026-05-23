using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TripPlanner.Domain.Compute;
using TripPlanner.Domain.Driver;
using TripPlanner.Domain.Passenger;
using TripPlanner.Infrastructure.Postgres;
using Xunit;

namespace TripPlanner.UnitTests.Integration;

// Integration tests for PostgresDriverRouteRepository.
// Requires a running Postgres instance with the trip-planner schema.
// Set TRIP_PLANNER_TEST_DB to override the connection string.
//
// Tests cover: persisting a new route, reading it back, updating to Active,
// querying the active route for a driver, and finding ride-request IDs
// whose match_results reference a given driver route.
public class PostgresDriverRouteRepositoryTests : IAsyncLifetime
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresDriverRouteRepositoryTests()
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
    public async Task AddAsync_ShouldPersistDriverRoute()
    {
        var repo = new PostgresDriverRouteRepository(new DbSession(_dataSource));

        var route = BuildRoute();
        await repo.AddAsync(route, CancellationToken.None);

        var loaded = await repo.GetByIdAsync(route.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(route.Id, loaded!.Id);
        Assert.Equal(route.DriverId, loaded.DriverId);
        Assert.Equal(DriverRouteStatus.Pending, loaded.Status);
        Assert.Equal(route.StartPoint, loaded.StartPoint);
        Assert.Equal(route.EndPoint, loaded.EndPoint);
        Assert.Null(loaded.RouteGeometryJson);
        Assert.Null(loaded.EtaSeconds);
        Assert.Null(loaded.DistanceMeters);
        Assert.Null(loaded.ActivatedAt);
    }

    // ─── UpdateAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ShouldPersistActivatedState()
    {
        var repo = new PostgresDriverRouteRepository(new DbSession(_dataSource));

        var route = BuildRoute();
        await repo.AddAsync(route, CancellationToken.None);

        route.Activate("""{"type":"LineString"}""", etaSeconds: 600, distanceMeters: 5000);
        await repo.UpdateAsync(route, CancellationToken.None);

        var loaded = await repo.GetByIdAsync(route.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(DriverRouteStatus.Active, loaded!.Status);
        Assert.Equal("""{"type":"LineString"}""", loaded.RouteGeometryJson);
        Assert.Equal(600, loaded.EtaSeconds);
        Assert.Equal(5000, loaded.DistanceMeters);
        Assert.NotNull(loaded.ActivatedAt);
    }

    [Fact]
    public async Task UpdateAsync_ShouldPersistCancelledState()
    {
        var repo = new PostgresDriverRouteRepository(new DbSession(_dataSource));

        var route = BuildRoute();
        await repo.AddAsync(route, CancellationToken.None);

        route.Cancel();
        await repo.UpdateAsync(route, CancellationToken.None);

        var loaded = await repo.GetByIdAsync(route.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(DriverRouteStatus.Cancelled, loaded!.Status);
        Assert.NotNull(loaded.CancelledAt);
    }

    // ─── GetActiveByDriverIdAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetActiveByDriverIdAsync_ShouldReturnActiveRoute()
    {
        var repo     = new PostgresDriverRouteRepository(new DbSession(_dataSource));
        var driverId = Guid.NewGuid();

        var route = BuildRoute(driverId: driverId);
        await repo.AddAsync(route, CancellationToken.None);

        route.Activate("""{"type":"LineString"}""", 300, 2500);
        await repo.UpdateAsync(route, CancellationToken.None);

        var found = await repo.GetActiveByDriverIdAsync(driverId, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(route.Id, found!.Id);
        Assert.Equal(DriverRouteStatus.Active, found.Status);
    }

    [Fact]
    public async Task GetActiveByDriverIdAsync_ShouldReturnNull_WhenNoActiveRoute()
    {
        var repo     = new PostgresDriverRouteRepository(new DbSession(_dataSource));
        var driverId = Guid.NewGuid();

        var route = BuildRoute(driverId: driverId);
        await repo.AddAsync(route, CancellationToken.None);
        // left in Pending — not Active

        var found = await repo.GetActiveByDriverIdAsync(driverId, CancellationToken.None);

        Assert.Null(found);
    }

    // ─── GetPendingRequestIdsForRouteAsync ────────────────────────────────────

    [Fact]
    public async Task GetPendingRequestIdsForRouteAsync_ShouldReturnMatchingRequestIds()
    {
        var routeRepo   = new PostgresDriverRouteRepository(new DbSession(_dataSource));
        var requestRepo = new PostgresRideRequestRepository(new DbSession(_dataSource));

        var driverRouteId = Guid.NewGuid();

        var matchingRequest = BuildRideRequest();
        matchingRequest.PresentMatches(new List<MatchEntry>
        {
            new(driverRouteId, Guid.NewGuid(), 120, 500, 0.9),
        });
        await requestRepo.AddAsync(matchingRequest, CancellationToken.None);
        await requestRepo.UpdateAsync(matchingRequest, CancellationToken.None);

        var unrelatedRequest = BuildRideRequest();
        unrelatedRequest.PresentMatches(new List<MatchEntry>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), 200, 800, 0.5),
        });
        await requestRepo.AddAsync(unrelatedRequest, CancellationToken.None);
        await requestRepo.UpdateAsync(unrelatedRequest, CancellationToken.None);

        var ids = await routeRepo.GetPendingRequestIdsForRouteAsync(driverRouteId, CancellationToken.None);

        Assert.Contains(matchingRequest.Id, ids);
        Assert.DoesNotContain(unrelatedRequest.Id, ids);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static DriverRoute BuildRoute(Guid? driverId = null) => new()
    {
        Id         = Guid.NewGuid(),
        DriverId   = driverId ?? Guid.NewGuid(),
        StartPoint = new GeoPoint(52.2297, 21.0122),
        EndPoint   = new GeoPoint(52.2500, 21.0300),
        CreatedAt  = DateTimeOffset.UtcNow,
    };

    private static RideRequest BuildRideRequest() => new()
    {
        Id          = Guid.NewGuid(),
        PassengerId = Guid.NewGuid(),
        StartPoint  = new GeoPoint(52.2297, 21.0122),
        EndPoint    = new GeoPoint(52.2500, 21.0300),
        Constraints = new MatchConstraints(5, 5),
        CreatedAt   = DateTimeOffset.UtcNow,
        UpdatedAt   = DateTimeOffset.UtcNow,
    };

    private async Task TruncateAsync()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE driver_routes, ride_requests, rides, route_jobs CASCADE";
        await cmd.ExecuteNonQueryAsync();
    }
}