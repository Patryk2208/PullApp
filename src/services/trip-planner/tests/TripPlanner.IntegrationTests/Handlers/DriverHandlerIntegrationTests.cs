using TripPlanner.Application.Metrics;
using NSubstitute;
using TripPlanner.Application.Features.Driver;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Compute;
using TripPlanner.Domain.RideRequest;
using TripPlanner.Domain.Route;
using TripPlanner.Infrastructure.Postgres;
using TripPlanner.IntegrationTests.Fixtures;

namespace TripPlanner.IntegrationTests.Handlers;

[Collection("Postgres")]
public class DriverHandlerIntegrationTests(PostgresFixture db) : IAsyncLifetime
{
    public Task InitializeAsync() => db.CleanAsync();
    public Task DisposeAsync()    => Task.CompletedTask;

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static readonly GeoPoint PointA = new(52.2, 21.0);
    private static readonly GeoPoint PointB = new(52.3, 21.1);

    private static Route SeedRoute(DbSession s, Guid? driverId = null, bool activate = false)
    {
        var route = Route.Create(driverId ?? Guid.NewGuid(), PointA, PointB, capacity: 3);
        new PostgresRouteRepository(s).AddAsync(route, default).GetAwaiter().GetResult();
        route.SetGeometry([PointA, PointB], 300.0, 5000.0);
        if (activate) route.Activate(PointA);
        new PostgresRouteRepository(s).UpdateAsync(route, default).GetAwaiter().GetResult();
        return route;
    }

    private async Task<Route?> LoadRoute(Guid id)
    {
        var s = db.NewSession();
        return await new PostgresRouteRepository(s).GetByIdAsync(id, default);
    }

    private async Task<RideRequest?> LoadRequest(Guid id)
    {
        var s = db.NewSession();
        return await new PostgresRideRequestRepository(s).GetByIdAsync(id, default);
    }

    private async Task<Domain.Ride.Ride?> LoadRide(Guid id)
    {
        var s = db.NewSession();
        return await new PostgresRideRepository(s).GetByIdAsync(id, default);
    }

    // ─── CreateRoute ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRoute_HappyPath_PersistsRouteAndJob()
    {
        var session  = db.NewSession();
        var accounts = Substitute.For<IAccountsService>();
        var geo      = Substitute.For<IGeoService>();
        var compute  = Substitute.For<IComputePublisher<ComputeJob>>();
        accounts.CanDriveAsync(Arg.Any<Guid>(), default).Returns(true);
        geo.IsWithinServiceAreaAsync(Arg.Any<GeoPoint>(), default).Returns(true);

        var handler = new CreateRouteHandler(
            new PostgresRouteRepository(session),
            new PostgresRouteJobRepository(session),
            compute, geo, accounts, new TripPlannerMetrics(), session, NullLogger<CreateRouteHandler>.Instance);

        var driverId = Guid.NewGuid();
        var result   = await handler.HandleAsync(new(driverId, PointA, PointB, Capacity: 2), default);

        var route = await LoadRoute(result.RouteId);
        Assert.NotNull(route);
        Assert.Equal(driverId, route!.DriverId);
        Assert.Equal(RouteStatus.Calculating, route.Status);
    }

    // ─── ActivateRoute ────────────────────────────────────────────────────────

    [Fact]
    public async Task ActivateRoute_HappyPath_RouteBecomesActiveInDb()
    {
        var driverId  = Guid.NewGuid();
        var seedSess  = db.NewSession();
        var route     = SeedRoute(seedSess, driverId);
        var geo       = Substitute.For<IGeoService>();
        geo.IsNearAsync(Arg.Any<GeoPoint>(), Arg.Any<GeoPoint>(), Arg.Any<double>(), default).Returns(true);

        var session = db.NewSession();
        var handler = new ActivateRouteHandler(
            new PostgresRouteRepository(session),
            new PostgresRideRepository(session),
            geo, session, NullLogger<ActivateRouteHandler>.Instance);

        await handler.HandleAsync(new(driverId, route.Id, PointA), default);

        var loaded = await LoadRoute(route.Id);
        Assert.Equal(RouteStatus.Active, loaded!.Status);
        Assert.NotNull(loaded.ActivatedAt);
    }

    // ─── DeleteRoute ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteRoute_HappyPath_RouteRemovedFromDb()
    {
        var driverId = Guid.NewGuid();
        var seedSess = db.NewSession();
        var route    = SeedRoute(seedSess, driverId);
        var payments = Substitute.For<IPaymentsService>();
        var events   = Substitute.For<IEventPublisher>();

        var session = db.NewSession();
        var handler = new DeleteRouteHandler(
            new PostgresRouteRepository(session),
            new PostgresRideRepository(session),
            new PostgresRideRequestRepository(session),
            payments, events, new KafkaTopics(), session, NullLogger<DeleteRouteHandler>.Instance);

        await handler.HandleAsync(new(driverId, route.Id), default);

        Assert.Null(await LoadRoute(route.Id));
    }

    // ─── RejectRideRequest ────────────────────────────────────────────────────

    [Fact]
    public async Task RejectRideRequest_HappyPath_RequestMarkedRejectedInDb()
    {
        var driverId = Guid.NewGuid();
        var seedSess = db.NewSession();
        var route    = SeedRoute(seedSess, driverId);
        var req      = RideRequest.Create(route.Id, Guid.NewGuid(), PointA, PointB);
        req.SetFrozenPrice(Guid.NewGuid(), 20m, 4m);
        await new PostgresRideRequestRepository(seedSess).AddAsync(req, default);
        var payments = Substitute.For<IPaymentsService>();
        var events   = Substitute.For<IEventPublisher>();

        var session = db.NewSession();
        var handler = new RejectRideRequestHandler(
            new PostgresRouteRepository(session),
            new PostgresRideRequestRepository(session),
            payments, events, new KafkaTopics(), new TripPlannerMetrics(), session, NullLogger<RejectRideRequestHandler>.Instance);

        await handler.HandleAsync(new(driverId, req.Id), default);

        var loaded = await LoadRequest(req.Id);
        Assert.Equal(RideRequestStatus.Rejected, loaded!.Status);
        Assert.NotNull(loaded.RejectedAt);
    }

    // ─── AcceptRideRequest ────────────────────────────────────────────────────

    [Fact]
    public async Task AcceptRideRequest_HappyPath_RideCreatedAndRequestAccepted()
    {
        var driverId = Guid.NewGuid();
        var seedSess = db.NewSession();
        var route    = SeedRoute(seedSess, driverId, activate: true);
        var req      = RideRequest.Create(route.Id, Guid.NewGuid(), PointA, PointB);
        req.SetFrozenPrice(Guid.NewGuid(), 20m, 4m);
        await new PostgresRideRequestRepository(seedSess).AddAsync(req, default);

        var chatRoomId = Guid.NewGuid();
        var payments   = Substitute.For<IPaymentsService>();
        var chat       = Substitute.For<IChatService>();
        var events     = Substitute.For<IEventPublisher>();
        chat.CreateRoomAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), default)
            .Returns(chatRoomId);

        var session = db.NewSession();
        var handler = new AcceptRideRequestHandler(
            new PostgresRouteRepository(session),
            new PostgresRideRequestRepository(session),
            new PostgresRideRepository(session),
            payments, chat, events, new KafkaTopics(), new TripPlannerMetrics(), session, NullLogger<AcceptRideRequestHandler>.Instance);

        var result = await handler.HandleAsync(new(driverId, req.Id), default);

        var ride    = await LoadRide(result.RideId);
        var request = await LoadRequest(req.Id);
        Assert.NotNull(ride);
        Assert.Equal(RideRequestStatus.Accepted, request!.Status);
        Assert.Equal(chatRoomId, ride!.ChatRoomId);
    }

    // ─── DeclareDriverPickup ──────────────────────────────────────────────────

    [Fact]
    public async Task DeclareDriverPickup_HappyPath_FlagSetInDb()
    {
        var driverId = Guid.NewGuid();
        var seedSess = db.NewSession();
        var ride     = Domain.Ride.Ride.Create(
            Guid.NewGuid(), driverId, Guid.NewGuid(),
            PointA, PointB, 20m, 4m, Guid.NewGuid(), routeIsActive: true);
        await new PostgresRideRepository(seedSess).AddAsync(ride, default);

        var session = db.NewSession();
        var handler = new DeclareDriverPickupHandler(new PostgresRideRepository(session), session, NullLogger<DeclareDriverPickupHandler>.Instance);

        await handler.HandleAsync(new(driverId, ride.Id), default);

        var loaded = await LoadRide(ride.Id);
        Assert.True(loaded!.DriverDeclaredPickup);
    }

    // ─── DeclareDriverEnd ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeclareDriverEnd_HappyPath_RideMarkedEndedInDb()
    {
        var driverId = Guid.NewGuid();
        var seedSess = db.NewSession();
        var route    = SeedRoute(seedSess, driverId, activate: true);
        var ride     = Domain.Ride.Ride.Create(
            route.Id, driverId, Guid.NewGuid(),
            PointA, PointB, 20m, 4m, Guid.NewGuid(), routeIsActive: true);
        ride.DeclareDriverPickup();
        ride.DeclarePassengerPickup();
        ride.DeclarePassengerEnd();
        await new PostgresRideRepository(seedSess).AddAsync(ride, default);

        var payments = Substitute.For<IPaymentsService>();
        var events   = Substitute.For<IEventPublisher>();

        var session = db.NewSession();
        var handler = new DeclareDriverEndHandler(
            new PostgresRideRepository(session),
            new PostgresRouteRepository(session),
            new PostgresRideRequestRepository(session),
            payments, events, new KafkaTopics(), new TripPlannerMetrics(), session, NullLogger<DeclareDriverEndHandler>.Instance);

        await handler.HandleAsync(new(driverId, ride.Id), default);

        var loaded = await LoadRide(ride.Id);
        Assert.True(loaded!.DriverDeclaredEnd);
        Assert.NotNull(loaded.EndedAt);
    }
}
