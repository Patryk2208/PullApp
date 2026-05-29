using TripPlanner.Application.Features.Driver;

namespace TripPlanner.UnitTests.Features.Driver;

public class RejectRideRequestHandlerTests
{
    private readonly IRouteRepository       _routes       = Substitute.For<IRouteRepository>();
    private readonly IRideRequestRepository _rideRequests = Substitute.For<IRideRequestRepository>();
    private readonly IPaymentsService       _payments     = Substitute.For<IPaymentsService>();
    private readonly IEventPublisher        _events       = Substitute.For<IEventPublisher>();
    private readonly IUnitOfWork            _uow          = Substitute.For<IUnitOfWork>();

    private RejectRideRequestHandler Handler() =>
        new(_routes, _rideRequests, _payments, _events, _uow);

    [Fact]
    public async Task HandleAsync_HappyPath_RejectsRequestAndPublishesEvent()
    {
        var driverId = Guid.NewGuid();
        var route    = Make.CreatedRoute(driverId);
        var req      = Make.PendingRequest(routeId: route.Id);
        _rideRequests.GetByIdAsync(req.Id, default).Returns(req);
        _routes.GetByIdAsync(route.Id, default).Returns(route);

        await Handler().HandleAsync(new(driverId, req.Id), default);

        Assert.Equal(RideRequestStatus.Rejected, req.Status);
        await _events.Received(1).PublishAsync(
            Topics.NotificationTriggers,
            Arg.Is<RideRejectedEvent>(e => e.RequestId == req.Id && e.PassengerId == req.PassengerId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithFrozenPrice_UnfreezesPassengerFunds()
    {
        var driverId = Guid.NewGuid();
        var route    = Make.CreatedRoute(driverId);
        var req      = Make.PendingRequest(routeId: route.Id);
        _rideRequests.GetByIdAsync(req.Id, default).Returns(req);
        _routes.GetByIdAsync(route.Id, default).Returns(route);

        await Handler().HandleAsync(new(driverId, req.Id), default);

        await _payments.Received(1).UnfreezeAsync(req.FrozenPriceId!.Value, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NoFrozenPrice_DoesNotCallUnfreeze()
    {
        var driverId = Guid.NewGuid();
        var route    = Make.CreatedRoute(driverId);
        var req      = RideRequest.Create(route.Id, Guid.NewGuid(), Make.PointA, Make.PointB); // no frozen price
        _rideRequests.GetByIdAsync(req.Id, default).Returns(req);
        _routes.GetByIdAsync(route.Id, default).Returns(route);

        await Handler().HandleAsync(new(driverId, req.Id), default);

        await _payments.DidNotReceiveWithAnyArgs().UnfreezeAsync(default, default);
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
        _routes.GetByIdAsync(req.RouteId, default).Returns((Route?)null);

        await Assert.ThrowsAsync<RouteNotFoundException>(() =>
            Handler().HandleAsync(new(Guid.NewGuid(), req.Id), default));
    }

    [Fact]
    public async Task HandleAsync_WrongDriver_ThrowsUnauthorizedException()
    {
        var route = Make.CreatedRoute();
        var req   = Make.PendingRequest(routeId: route.Id);
        _rideRequests.GetByIdAsync(req.Id, default).Returns(req);
        _routes.GetByIdAsync(route.Id, default).Returns(route);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            Handler().HandleAsync(new(Guid.NewGuid(), req.Id), default));
    }

    [Fact]
    public async Task HandleAsync_RequestNotPending_ThrowsInvalidRouteStatusException()
    {
        var driverId = Guid.NewGuid();
        var route    = Make.CreatedRoute(driverId);
        var req      = Make.PendingRequest(routeId: route.Id);
        req.Accept(); // no longer Pending
        _rideRequests.GetByIdAsync(req.Id, default).Returns(req);
        _routes.GetByIdAsync(route.Id, default).Returns(route);

        await Assert.ThrowsAsync<InvalidRouteStatusException>(() =>
            Handler().HandleAsync(new(driverId, req.Id), default));
    }
}
