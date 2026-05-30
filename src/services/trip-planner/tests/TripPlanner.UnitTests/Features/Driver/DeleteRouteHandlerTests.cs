using TripPlanner.Application.Features.Driver;

namespace TripPlanner.UnitTests.Features.Driver;

public class DeleteRouteHandlerTests
{
    private readonly IRouteRepository       _routes       = Substitute.For<IRouteRepository>();
    private readonly IRideRepository        _rides        = Substitute.For<IRideRepository>();
    private readonly IRideRequestRepository _rideRequests = Substitute.For<IRideRequestRepository>();
    private readonly IPaymentsService       _payments     = Substitute.For<IPaymentsService>();
    private readonly IEventPublisher        _events       = Substitute.For<IEventPublisher>();
    private readonly IUnitOfWork            _uow          = Substitute.For<IUnitOfWork>();

    private DeleteRouteHandler Handler() =>
        new(_routes, _rides, _rideRequests, _payments, _events, _uow, NullLogger<DeleteRouteHandler>.Instance);

    [Fact]
    public async Task HandleAsync_ActiveRouteWithRides_ThrowsRouteNotDeletableException()
    {
        var driverId = Guid.NewGuid();
        var route    = Make.ActiveRoute(driverId);
        var ride     = Make.WaitingForDriverRide(routeId: route.Id, driverId: driverId);
        _routes.GetByIdAsync(route.Id, default).Returns(route);
        _rides.GetActiveByRouteIdAsync(route.Id, default).Returns(new List<Ride> { ride });

        await Assert.ThrowsAsync<RouteNotDeletableException>(() =>
            Handler().HandleAsync(new(driverId, route.Id), default));
    }

    [Fact]
    public async Task HandleAsync_CreatedRouteWithRides_DeletesAndPublishesEvent()
    {
        var driverId    = Guid.NewGuid();
        var passengerId = Guid.NewGuid();
        var route       = Make.CreatedRoute(driverId);
        var ride        = Make.WaitingForActivationRide(routeId: route.Id, driverId: driverId, passengerId: passengerId);
        _routes.GetByIdAsync(route.Id, default).Returns(route);
        _rides.GetActiveByRouteIdAsync(route.Id, default).Returns(new List<Ride> { ride });
        _rideRequests.GetPendingByRouteIdAsync(route.Id, default).Returns(new List<RideRequest>());

        await Handler().HandleAsync(new(driverId, route.Id), default);

        await _routes.Received(1).DeleteAsync(route.Id, Arg.Any<CancellationToken>());
        await _events.Received(1).PublishAsync(
            Topics.NotificationTriggers,
            Arg.Is<RouteDeletedEvent>(e => e.AffectedPassengerIds.Contains(passengerId)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NoRides_DeletesSilentlyWithoutEvent()
    {
        var driverId = Guid.NewGuid();
        var route    = Make.ActiveRoute(driverId);
        _routes.GetByIdAsync(route.Id, default).Returns(route);
        _rides.GetActiveByRouteIdAsync(route.Id, default).Returns(new List<Ride>());
        _rideRequests.GetPendingByRouteIdAsync(route.Id, default).Returns(new List<RideRequest>());

        await Handler().HandleAsync(new(driverId, route.Id), default);

        await _routes.Received(1).DeleteAsync(route.Id, Arg.Any<CancellationToken>());
        await _events.DidNotReceive().PublishAsync(
            Arg.Any<string>(), Arg.Any<RouteDeletedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PendingRequests_UnfreezesAndRejectsThem()
    {
        var driverId = Guid.NewGuid();
        var route    = Make.CreatedRoute(driverId);
        var req      = Make.PendingRequest(routeId: route.Id);
        _routes.GetByIdAsync(route.Id, default).Returns(route);
        _rides.GetActiveByRouteIdAsync(route.Id, default).Returns(new List<Ride>());
        _rideRequests.GetPendingByRouteIdAsync(route.Id, default).Returns(new List<RideRequest> { req });

        await Handler().HandleAsync(new(driverId, route.Id), default);

        await _payments.Received(1).UnfreezeAsync(req.FrozenPriceId!.Value, Arg.Any<CancellationToken>());
        Assert.Equal(RideRequestStatus.Rejected, req.Status);
    }

    [Fact]
    public async Task HandleAsync_RouteNotFound_ThrowsRouteNotFoundException()
    {
        _routes.GetByIdAsync(Arg.Any<Guid>(), default).Returns((Route?)null);

        await Assert.ThrowsAsync<RouteNotFoundException>(() =>
            Handler().HandleAsync(new(Guid.NewGuid(), Guid.NewGuid()), default));
    }

    [Fact]
    public async Task HandleAsync_WrongDriver_ThrowsUnauthorizedException()
    {
        var route = Make.ActiveRoute();
        _routes.GetByIdAsync(route.Id, default).Returns(route);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            Handler().HandleAsync(new(Guid.NewGuid(), route.Id), default));
    }

    [Fact]
    public async Task HandleAsync_RideWithFrozenPrice_UnfreezesCalled()
    {
        var driverId = Guid.NewGuid();
        var route    = Make.CreatedRoute(driverId);
        var ride     = Make.WaitingForActivationRide(routeId: route.Id, driverId: driverId);
        _routes.GetByIdAsync(route.Id, default).Returns(route);
        _rides.GetActiveByRouteIdAsync(route.Id, default).Returns(new List<Ride> { ride });
        _rideRequests.GetPendingByRouteIdAsync(route.Id, default).Returns(new List<RideRequest>());

        await Handler().HandleAsync(new(driverId, route.Id), default);

        await _payments.Received(1).UnfreezeAsync(ride.FrozenPriceId!.Value, Arg.Any<CancellationToken>());
    }
}
