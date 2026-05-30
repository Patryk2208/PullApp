using TripPlanner.Application.Features.Driver;

namespace TripPlanner.UnitTests.Features.Driver;

public class AcceptRideRequestHandlerTests
{
    private readonly IRouteRepository       _routes       = Substitute.For<IRouteRepository>();
    private readonly IRideRequestRepository _rideRequests = Substitute.For<IRideRequestRepository>();
    private readonly IRideRepository        _rides        = Substitute.For<IRideRepository>();
    private readonly IPaymentsService       _payments     = Substitute.For<IPaymentsService>();
    private readonly IChatService           _chat         = Substitute.For<IChatService>();
    private readonly IEventPublisher        _events       = Substitute.For<IEventPublisher>();
    private readonly IUnitOfWork            _uow          = Substitute.For<IUnitOfWork>();

    private AcceptRideRequestHandler Handler() =>
        new(_routes, _rideRequests, _rides, _payments, _chat, _events, new TripPlannerMetrics(), _uow);

    private void SetupHappyPath(RideRequest req, Route route)
    {
        _rideRequests.GetByIdAsync(req.Id, default).Returns(req);
        _routes.GetByIdForUpdateAsync(route.Id, default).Returns(route);
        _rideRequests.GetPendingByRouteIdAsync(route.Id, default).Returns(new List<RideRequest>());
        _chat.CreateRoomAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), default)
             .Returns(Guid.NewGuid());
    }

    [Fact]
    public async Task HandleAsync_HappyPath_ReturnsRideIdAndChatRoomId()
    {
        var driverId = Guid.NewGuid();
        var route    = Make.ActiveRoute(driverId);
        var req      = Make.PendingRequest(routeId: route.Id);
        SetupHappyPath(req, route);

        var result = await Handler().HandleAsync(new(driverId, req.Id), default);

        Assert.NotEqual(Guid.Empty, result.RideId);
        Assert.NotNull(result.ChatRoomId);
    }

    [Fact]
    public async Task HandleAsync_HappyPath_AcceptsRequestAndCreatesRide()
    {
        var driverId = Guid.NewGuid();
        var route    = Make.ActiveRoute(driverId);
        var req      = Make.PendingRequest(routeId: route.Id);
        SetupHappyPath(req, route);

        await Handler().HandleAsync(new(driverId, req.Id), default);

        Assert.Equal(RideRequestStatus.Accepted, req.Status);
        await _rides.Received(1).AddAsync(Arg.Any<Ride>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_RouteWasActive_RideStartsAsWaitingForDriver()
    {
        var driverId = Guid.NewGuid();
        var route    = Make.ActiveRoute(driverId);
        var req      = Make.PendingRequest(routeId: route.Id);
        SetupHappyPath(req, route);

        Ride? createdRide = null;
        await _rides.AddAsync(Arg.Do<Ride>(r => createdRide = r), Arg.Any<CancellationToken>());

        await Handler().HandleAsync(new(driverId, req.Id), default);

        Assert.NotNull(createdRide);
        Assert.Equal(RideStatus.WaitingForDriver, createdRide!.Status);
    }

    [Fact]
    public async Task HandleAsync_RouteBecomesFullAfterAccept_RejectsPendingRequests()
    {
        var driverId   = Guid.NewGuid();
        var route      = Make.ActiveRoute(driverId, capacity: 1); // capacity 1, accept will fill it
        var req        = Make.PendingRequest(routeId: route.Id);
        var otherReq   = Make.PendingRequest(routeId: route.Id);
        _rideRequests.GetByIdAsync(req.Id, default).Returns(req);
        _routes.GetByIdForUpdateAsync(route.Id, default).Returns(route);
        _rideRequests.GetPendingByRouteIdAsync(route.Id, default).Returns(new List<RideRequest> { otherReq });
        _chat.CreateRoomAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), default).Returns(Guid.NewGuid());

        await Handler().HandleAsync(new(driverId, req.Id), default);

        Assert.Equal(RideRequestStatus.Rejected, otherReq.Status);
        await _payments.Received(1).UnfreezeAsync(otherReq.FrozenPriceId!.Value, Arg.Any<CancellationToken>());
        await _events.Received(1).PublishAsync(
            Topics.NotificationTriggers,
            Arg.Is<RideRejectedEvent>(e => e.RequestId == otherReq.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_DbError_RollsBackAndUnfreezesPassengerFunds()
    {
        var driverId = Guid.NewGuid();
        var route    = Make.ActiveRoute(driverId);
        var req      = Make.PendingRequest(routeId: route.Id);
        _rideRequests.GetByIdAsync(req.Id, default).Returns(req);
        _routes.GetByIdForUpdateAsync(route.Id, default).Returns(route);
        _rides.AddAsync(Arg.Any<Ride>(), Arg.Any<CancellationToken>())
              .Returns(_ => throw new InvalidOperationException("db error"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler().HandleAsync(new(driverId, req.Id), default));

        await _uow.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _payments.Received(1).UnfreezeAsync(req.FrozenPriceId!.Value, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_RequestNotFound_ThrowsRideRequestNotFoundException()
    {
        _rideRequests.GetByIdAsync(Arg.Any<Guid>(), default).Returns((RideRequest?)null);

        await Assert.ThrowsAsync<RideRequestNotFoundException>(() =>
            Handler().HandleAsync(new(Guid.NewGuid(), Guid.NewGuid()), default));
    }

    [Fact]
    public async Task HandleAsync_RouteNotFound_ThrowsRouteNotFoundException()
    {
        var req = Make.PendingRequest();
        _rideRequests.GetByIdAsync(req.Id, default).Returns(req);
        _routes.GetByIdForUpdateAsync(req.RouteId, default).Returns((Route?)null);

        await Assert.ThrowsAsync<RouteNotFoundException>(() =>
            Handler().HandleAsync(new(Guid.NewGuid(), req.Id), default));
    }

    [Fact]
    public async Task HandleAsync_RequestNotPending_ThrowsInvalidRouteStatusException()
    {
        var req = Make.PendingRequest();
        req.Accept();
        _rideRequests.GetByIdAsync(req.Id, default).Returns(req);

        await Assert.ThrowsAsync<InvalidRouteStatusException>(() =>
            Handler().HandleAsync(new(Guid.NewGuid(), req.Id), default));
    }

    [Fact]
    public async Task HandleAsync_WrongDriver_ThrowsUnauthorizedException()
    {
        var route = Make.ActiveRoute();
        var req   = Make.PendingRequest(routeId: route.Id);
        _rideRequests.GetByIdAsync(req.Id, default).Returns(req);
        _routes.GetByIdForUpdateAsync(route.Id, default).Returns(route);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            Handler().HandleAsync(new(Guid.NewGuid(), req.Id), default));
    }

    [Fact]
    public async Task HandleAsync_RouteCalculating_ThrowsInvalidRouteStatusException()
    {
        var driverId = Guid.NewGuid();
        var route    = Make.CalculatingRoute(driverId);
        var req      = Make.PendingRequest(routeId: route.Id);
        _rideRequests.GetByIdAsync(req.Id, default).Returns(req);
        _routes.GetByIdForUpdateAsync(route.Id, default).Returns(route);

        await Assert.ThrowsAsync<InvalidRouteStatusException>(() =>
            Handler().HandleAsync(new(driverId, req.Id), default));
    }

    [Fact]
    public async Task HandleAsync_RouteAlreadyFull_ThrowsRouteFullException()
    {
        var driverId = Guid.NewGuid();
        var route    = Make.ActiveRoute(driverId, capacity: 1);
        route.TryAddRide(); // fill the route → Full
        var req = Make.PendingRequest(routeId: route.Id);
        _rideRequests.GetByIdAsync(req.Id, default).Returns(req);
        _routes.GetByIdForUpdateAsync(route.Id, default).Returns(route);

        await Assert.ThrowsAsync<RouteFullException>(() =>
            Handler().HandleAsync(new(driverId, req.Id), default));
    }
}
