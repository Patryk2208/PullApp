using TripPlanner.Application.Features.Driver;

namespace TripPlanner.UnitTests.Features.Driver;

public class DeclareDriverEndHandlerTests
{
    private readonly IRideRepository        _rides        = Substitute.For<IRideRepository>();
    private readonly IRouteRepository       _routes       = Substitute.For<IRouteRepository>();
    private readonly IRideRequestRepository _rideRequests = Substitute.For<IRideRequestRepository>();
    private readonly IPaymentsService       _payments     = Substitute.For<IPaymentsService>();
    private readonly IEventPublisher        _events       = Substitute.For<IEventPublisher>();
    private readonly IUnitOfWork            _uow          = Substitute.For<IUnitOfWork>();

    private DeclareDriverEndHandler Handler() =>
        new(_rides, _routes, _rideRequests, _payments, _events, _uow);

    private static Ride StartedRideWithPassengerEnd(Guid driverId, Guid routeId)
    {
        var ride = Make.StartedRide(routeId: routeId, driverId: driverId);
        ride.DeclarePassengerEnd();
        return ride;
    }

    [Fact]
    public async Task HandleAsync_HappyPath_ChargesPaymentAndPublishesEvents()
    {
        var driverId = Guid.NewGuid();
        var routeId  = Guid.NewGuid();
        var route    = Make.ActiveRoute(driverId);
        var ride     = StartedRideWithPassengerEnd(driverId, routeId);
        _rides.GetByIdAsync(ride.Id, default).Returns(ride);
        _routes.GetByIdAsync(ride.RouteId, default).Returns(route);
        _rideRequests.GetRejectedByRouteIdAsync(ride.RouteId, default).Returns(new List<RideRequest>());

        await Handler().HandleAsync(new(driverId, ride.Id), default);

        await _payments.Received(1).ChargeAsync(ride.FrozenPriceId!.Value, Arg.Any<CancellationToken>());
        await _events.Received(1).PublishAsync(
            Topics.NotificationTriggers, Arg.Any<RideEndedEvent>(), Arg.Any<CancellationToken>());
        await _events.Received(1).PublishAsync(
            Topics.RideCompletions, Arg.Any<RideCompletedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PassengerHasNotDeclaredEnd_ThrowsDeclarationOrderException()
    {
        var driverId = Guid.NewGuid();
        var ride     = Make.StartedRide(driverId: driverId);
        // passenger has NOT declared end
        _rides.GetByIdAsync(ride.Id, default).Returns(ride);

        await Assert.ThrowsAsync<DeclarationOrderException>(() =>
            Handler().HandleAsync(new(driverId, ride.Id), default));
    }

    [Fact]
    public async Task HandleAsync_WithRejectedPassengers_IncludesThemInRideEndedEvent()
    {
        var driverId    = Guid.NewGuid();
        var routeId     = Guid.NewGuid();
        var route       = Make.ActiveRoute(driverId);
        var ride        = StartedRideWithPassengerEnd(driverId, routeId);
        var rejectedReq = Make.PendingRequest(routeId: ride.RouteId);
        rejectedReq.Reject();
        _rides.GetByIdAsync(ride.Id, default).Returns(ride);
        _routes.GetByIdAsync(ride.RouteId, default).Returns(route);
        _rideRequests.GetRejectedByRouteIdAsync(ride.RouteId, default)
                     .Returns(new List<RideRequest> { rejectedReq });

        await Handler().HandleAsync(new(driverId, ride.Id), default);

        await _events.Received(1).PublishAsync(
            Topics.NotificationTriggers,
            Arg.Is<RideEndedEvent>(e => e.NotifyPassengerIds.Contains(rejectedReq.PassengerId)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_RouteFound_CallsRemoveRide()
    {
        var driverId = Guid.NewGuid();
        var route    = Make.ActiveRoute(driverId, capacity: 2);
        route.TryAddRide(); // simulate one active ride before the one being ended
        var ride = StartedRideWithPassengerEnd(driverId, route.Id);
        _rides.GetByIdAsync(ride.Id, default).Returns(ride);
        _routes.GetByIdAsync(ride.RouteId, default).Returns(route);
        _rideRequests.GetRejectedByRouteIdAsync(ride.RouteId, default).Returns(new List<RideRequest>());

        await Handler().HandleAsync(new(driverId, ride.Id), default);

        await _routes.Received(1).UpdateAsync(route, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_RideNotFound_ThrowsRideNotFoundException()
    {
        _rides.GetByIdAsync(Arg.Any<Guid>(), default).Returns((Ride?)null);

        await Assert.ThrowsAsync<RideNotFoundException>(() =>
            Handler().HandleAsync(new(Guid.NewGuid(), Guid.NewGuid()), default));
    }

    [Fact]
    public async Task HandleAsync_WrongDriver_ThrowsUnauthorizedException()
    {
        var ride = Make.StartedRide();
        _rides.GetByIdAsync(ride.Id, default).Returns(ride);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            Handler().HandleAsync(new(Guid.NewGuid(), ride.Id), default));
    }

    [Fact]
    public async Task HandleAsync_NotStartedStatus_ThrowsInvalidRouteStatusException()
    {
        var driverId = Guid.NewGuid();
        var ride     = Make.WaitingForDriverRide(driverId: driverId);
        _rides.GetByIdAsync(ride.Id, default).Returns(ride);

        await Assert.ThrowsAsync<InvalidRouteStatusException>(() =>
            Handler().HandleAsync(new(driverId, ride.Id), default));
    }
}
