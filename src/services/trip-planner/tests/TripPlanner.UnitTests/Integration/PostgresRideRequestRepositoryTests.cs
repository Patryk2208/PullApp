using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TripPlanner.Domain.Compute;
using TripPlanner.Domain.Passenger;
using TripPlanner.Infrastructure.Postgres;
using Xunit;

namespace TripPlanner.UnitTests.Integration;

// Integration tests for PostgresRideRequestRepository.
// Requires a running Postgres instance with the trip-planner schema.
// Set TRIP_PLANNER_TEST_DB to override the connection string.
//
// Tests cover: persisting a request, retrieving it by ID, querying for
// the active request for a passenger, updating match results and status
// transitions, and returning only expired PendingDriver confirmations.
public class PostgresRideRequestRepositoryTests : IAsyncLifetime
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresRideRequestRepositoryTests()
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
    public async Task AddAsync_ShouldPersistRideRequest()
    {
        var repo = new PostgresRideRequestRepository(new DbSession(_dataSource));

        var request = BuildRequest();
        await repo.AddAsync(request, CancellationToken.None);

        var loaded = await repo.GetByIdAsync(request.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(request.Id, loaded!.Id);
        Assert.Equal(request.PassengerId, loaded.PassengerId);
        Assert.Equal(RideRequestStatus.Searching, loaded.Status);
        Assert.Equal(request.StartPoint, loaded.StartPoint);
        Assert.Equal(request.EndPoint, loaded.EndPoint);
        Assert.Equal(request.Constraints.MaxDetourKm, loaded.Constraints.MaxDetourKm);
        Assert.Equal(request.Constraints.MaxResults, loaded.Constraints.MaxResults);
        Assert.Null(loaded.MatchResults);
        Assert.Null(loaded.SelectedRouteId);
        Assert.Null(loaded.JobId);
    }

    // ─── GetActiveByPassengerIdAsync ──────────────────────────────────────────

    [Fact]
    public async Task GetActiveByPassengerIdAsync_ShouldReturnSearchingRequest()
    {
        var repo        = new PostgresRideRequestRepository(new DbSession(_dataSource));
        var passengerId = Guid.NewGuid();

        var request = BuildRequest(passengerId: passengerId);
        await repo.AddAsync(request, CancellationToken.None);

        var found = await repo.GetActiveByPassengerIdAsync(passengerId, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(request.Id, found!.Id);
    }

    [Fact]
    public async Task GetActiveByPassengerIdAsync_ShouldReturnNull_WhenCancelled()
    {
        var repo        = new PostgresRideRequestRepository(new DbSession(_dataSource));
        var passengerId = Guid.NewGuid();

        var request = BuildRequest(passengerId: passengerId);
        await repo.AddAsync(request, CancellationToken.None);

        request.Cancel();
        await repo.UpdateAsync(request, CancellationToken.None);

        var found = await repo.GetActiveByPassengerIdAsync(passengerId, CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task GetActiveByPassengerIdAsync_ShouldReturnNull_WhenNoMatch()
    {
        var repo        = new PostgresRideRequestRepository(new DbSession(_dataSource));
        var passengerId = Guid.NewGuid();

        var request = BuildRequest(passengerId: passengerId);
        await repo.AddAsync(request, CancellationToken.None);

        request.MarkNoMatch();
        await repo.UpdateAsync(request, CancellationToken.None);

        var found = await repo.GetActiveByPassengerIdAsync(passengerId, CancellationToken.None);

        Assert.Null(found);
    }

    // ─── UpdateAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ShouldPersistMatchResults()
    {
        var repo = new PostgresRideRequestRepository(new DbSession(_dataSource));

        var request = BuildRequest();
        await repo.AddAsync(request, CancellationToken.None);

        var matches = new List<MatchEntry>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), 120, 500, 0.9),
            new(Guid.NewGuid(), Guid.NewGuid(), 200, 900, 0.7),
        };
        request.PresentMatches(matches);
        await repo.UpdateAsync(request, CancellationToken.None);

        var loaded = await repo.GetByIdAsync(request.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(RideRequestStatus.RoutesPresented, loaded!.Status);
        Assert.NotNull(loaded.MatchResults);
        Assert.Equal(2, loaded.MatchResults!.Count);
        Assert.Equal(matches[0].DriverRouteId, loaded.MatchResults[0].DriverRouteId);
        Assert.Equal(matches[1].DriverRouteId, loaded.MatchResults[1].DriverRouteId);
    }

    [Fact]
    public async Task UpdateAsync_ShouldPersistSelectedRoute()
    {
        var repo = new PostgresRideRequestRepository(new DbSession(_dataSource));

        var request = BuildRequest();
        await repo.AddAsync(request, CancellationToken.None);

        var driverRouteId = Guid.NewGuid();
        request.PresentMatches(new List<MatchEntry> { new(driverRouteId, Guid.NewGuid(), 90, 400, 0.95) });
        await repo.UpdateAsync(request, CancellationToken.None);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        request.SelectRoute(driverRouteId, deadline);
        await repo.UpdateAsync(request, CancellationToken.None);

        var loaded = await repo.GetByIdAsync(request.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(RideRequestStatus.PendingDriver, loaded!.Status);
        Assert.Equal(driverRouteId, loaded.SelectedRouteId);
        Assert.NotNull(loaded.ConfirmationDeadline);
    }

    // ─── GetExpiredConfirmationsAsync ─────────────────────────────────────────

    [Fact]
    public async Task GetExpiredConfirmationsAsync_ShouldReturnOnlyExpiredPendingDriverRequests()
    {
        var repo = new PostgresRideRequestRepository(new DbSession(_dataSource));

        var expiredRequest = BuildRequest();
        var driverRouteId  = Guid.NewGuid();
        expiredRequest.PresentMatches(new List<MatchEntry> { new(driverRouteId, Guid.NewGuid(), 60, 300, 0.8) });
        expiredRequest.SelectRoute(driverRouteId, DateTimeOffset.UtcNow.AddHours(-1)); // deadline in the past
        await repo.AddAsync(expiredRequest, CancellationToken.None);
        await repo.UpdateAsync(expiredRequest, CancellationToken.None);

        var freshRequest = BuildRequest();
        var otherRouteId = Guid.NewGuid();
        freshRequest.PresentMatches(new List<MatchEntry> { new(otherRouteId, Guid.NewGuid(), 60, 300, 0.8) });
        freshRequest.SelectRoute(otherRouteId, DateTimeOffset.UtcNow.AddHours(1)); // deadline in the future
        await repo.AddAsync(freshRequest, CancellationToken.None);
        await repo.UpdateAsync(freshRequest, CancellationToken.None);

        var searchingRequest = BuildRequest(); // never progressed to PendingDriver
        await repo.AddAsync(searchingRequest, CancellationToken.None);

        var result = await repo.GetExpiredConfirmationsAsync(CancellationToken.None);

        Assert.Contains(result, r => r.Id == expiredRequest.Id);
        Assert.DoesNotContain(result, r => r.Id == freshRequest.Id);
        Assert.DoesNotContain(result, r => r.Id == searchingRequest.Id);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static RideRequest BuildRequest(Guid? passengerId = null) => new()
    {
        Id          = Guid.NewGuid(),
        PassengerId = passengerId ?? Guid.NewGuid(),
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