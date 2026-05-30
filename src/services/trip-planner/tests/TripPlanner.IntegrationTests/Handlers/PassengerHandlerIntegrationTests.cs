using NSubstitute;
using TripPlanner.Application.Features.Passenger;
using TripPlanner.Application.Services;
using TripPlanner.Domain;
using TripPlanner.Domain.Compute;
using TripPlanner.Domain.Ride;
using TripPlanner.Domain.RideRequest;
using TripPlanner.Domain.Route;
using TripPlanner.Infrastructure.Postgres;
using TripPlanner.IntegrationTests.Fixtures;

namespace TripPlanner.IntegrationTests.Handlers;

[Collection("Postgres")]
public class PassengerHandlerIntegrationTests(PostgresFixture db) : IAsyncLifetime
{
    public Task InitializeAsync() => db.CleanAsync();
    public Task DisposeAsync()    => Task.CompletedTask;

    private static readonly GeoPoint PointA = new(52.2, 21.0);
    private static readonly GeoPoint PointB = new(52.3, 21.1);

    private async Task<Ride?> LoadRide(Guid id) =>
        await new PostgresRideRepository(db.NewSession()).GetByIdAsync(id, default);

    private async Task<RideRequest?> LoadRequest(Guid id) =>
        await new PostgresRideRequestRepository(db.NewSession()).GetByIdAsync(id, default);

    private async Task<RouteJob?> LoadJob(Guid id) =>
        await new PostgresRouteJobRepository(db.NewSession()).GetByIdAsync(id, default);

    // ─── SubmitRouteSearch ────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitRouteSearch_HappyPath_JobPersistedInDb()
    {
        var geo     = Substitute.For<IGeoService>();
        var compute = Substitute.For<IComputePublisher<ComputeJob>>();
        geo.IsWithinServiceAreaAsync(Arg.Any<GeoPoint>(), default).Returns(true);

        var session = db.NewSession();
        var handler = new SubmitRouteSearchHandler(
            new PostgresRouteJobRepository(session), compute, geo, session);

        var result = await handler.HandleAsync(new(Guid.NewGuid(), PointA, PointB), default);

        var job = await LoadJob(result.JobId);
        Assert.NotNull(job);
        Assert.Equal(JobType.PassengerMatch, job!.JobType);
        Assert.Equal(JobStatus.Pending, job.Status);
    }

    // ─── CreateRideRequest ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRideRequest_HappyPath_RequestPersistedWithPriceInDb()
    {
        var driverId = Guid.NewGuid();
        var route    = Route.Create(driverId, PointA, PointB, capacity: 3);
        route.SetGeometry("{}", 300, 5000);
        var seedSess = db.NewSession();
        await new PostgresRouteRepository(seedSess).AddAsync(route, default);
        await new PostgresRouteRepository(seedSess).UpdateAsync(route, default);

        var frozenId = Guid.NewGuid();
        var geo      = Substitute.For<IGeoService>();
        var payments = Substitute.For<IPaymentsService>();
        var events   = Substitute.For<IEventPublisher>();
        geo.IsWithinServiceAreaAsync(Arg.Any<GeoPoint>(), default).Returns(true);
        payments.QuoteAndFreezeAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<GeoPoint>(), Arg.Any<GeoPoint>(), default)
            .Returns(new PriceQuote(frozenId, 22m, 5m, "PLN", DateTimeOffset.UtcNow.AddMinutes(10)));

        var session = db.NewSession();
        var handler = new CreateRideRequestHandler(
            new PostgresRouteRepository(session),
            new PostgresRideRequestRepository(session),
            payments, geo, events, session);

        var result = await handler.HandleAsync(
            new(Guid.NewGuid(), route.Id, PointA, PointB), default);

        var req = await LoadRequest(result.RequestId);
        Assert.NotNull(req);
        Assert.Equal(RideRequestStatus.Pending, req!.Status);
        Assert.Equal(frozenId, req.FrozenPriceId);
        Assert.Equal(22m, req.Price);
        Assert.Equal(5m,  req.CancellationPrice);
    }

    // ─── CancelRide ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelRide_HappyPath_RideMarkedEndedInDb()
    {
        var passengerId = Guid.NewGuid();
        var ride        = Ride.Create(
            Guid.NewGuid(), Guid.NewGuid(), passengerId,
            PointA, PointB, 20m, 4m, Guid.NewGuid(), routeIsActive: false);
        var seedSess = db.NewSession();
        await new PostgresRideRepository(seedSess).AddAsync(ride, default);

        var payments = Substitute.For<IPaymentsService>();
        var events   = Substitute.For<IEventPublisher>();

        var session = db.NewSession();
        var handler = new CancelRideHandler(
            new PostgresRideRepository(session),
            new PostgresRouteRepository(session),
            new PostgresRideRequestRepository(session),
            payments, events, session);

        await handler.HandleAsync(new(passengerId, ride.Id), default);

        var loaded = await LoadRide(ride.Id);
        Assert.NotNull(loaded!.EndedAt);
    }

    // ─── DeclarePassengerPickup ───────────────────────────────────────────────

    [Fact]
    public async Task DeclarePassengerPickup_HappyPath_RideStartedInDb()
    {
        var passengerId = Guid.NewGuid();
        var ride        = Ride.Create(
            Guid.NewGuid(), Guid.NewGuid(), passengerId,
            PointA, PointB, 20m, 4m, Guid.NewGuid(), routeIsActive: true);
        ride.DeclareDriverPickup();
        var seedSess = db.NewSession();
        await new PostgresRideRepository(seedSess).AddAsync(ride, default);
        await new PostgresRideRepository(seedSess).UpdateAsync(ride, default);

        var session = db.NewSession();
        var handler = new DeclarePassengerPickupHandler(new PostgresRideRepository(session), session);

        await handler.HandleAsync(new(passengerId, ride.Id), default);

        var loaded = await LoadRide(ride.Id);
        Assert.Equal(RideStatus.Started, loaded!.Status);
        Assert.True(loaded.PassengerDeclaredPickup);
        Assert.NotNull(loaded.StartedAt);
    }

    // ─── DeclarePassengerEnd ──────────────────────────────────────────────────

    [Fact]
    public async Task DeclarePassengerEnd_HappyPath_FlagSetInDb()
    {
        var passengerId = Guid.NewGuid();
        var ride        = Ride.Create(
            Guid.NewGuid(), Guid.NewGuid(), passengerId,
            PointA, PointB, 20m, 4m, Guid.NewGuid(), routeIsActive: true);
        ride.DeclareDriverPickup();
        ride.DeclarePassengerPickup();
        var seedSess = db.NewSession();
        await new PostgresRideRepository(seedSess).AddAsync(ride, default);
        await new PostgresRideRepository(seedSess).UpdateAsync(ride, default);

        var session = db.NewSession();
        var handler = new DeclarePassengerEndHandler(new PostgresRideRepository(session), session);

        await handler.HandleAsync(new(passengerId, ride.Id), default);

        var loaded = await LoadRide(ride.Id);
        Assert.True(loaded!.PassengerDeclaredEnd);
    }
}
