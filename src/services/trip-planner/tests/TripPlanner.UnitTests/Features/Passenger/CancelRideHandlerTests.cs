using TripPlanner.Application.Features.Passenger;

namespace TripPlanner.UnitTests.Features.Passenger;

public class CancelRideHandlerTests
{
    private readonly IRideRepository        _rides        = Substitute.For<IRideRepository>();
    private readonly IRouteRepository       _routes       = Substitute.For<IRouteRepository>();
    private readonly IRideRequestRepository _rideRequests = Substitute.For<IRideRequestRepository>();
    private readonly IPaymentsService       _payments     = Substitute.For<IPaymentsService>();
    private readonly IEventPublisher        _events       = Substitute.For<IEventPublisher>();
    private readonly IUnitOfWork            _uow          = Substitute.For<IUnitOfWork>();

    private CancelRideHandler Handler() =>
        new(_rides, _routes, _rideRequests, _payments, _events, new KafkaTopics(), new TripPlannerMetrics(), _uow, NullLogger<CancelRideHandler>.Instance);

    [Fact]
    public async Task HandleAsync_WaitingForActivation_UnfreezesFundsAndPublishesEvents()
    {
        var passengerId = Guid.NewGuid();
        var ride        = Make.WaitingForActivationRide(passengerId: passengerId);
        _rides.GetByIdAsync(ride.Id, default).Returns(ride);
        _routes.GetByIdAsync(ride.RouteId, default).Returns((Route?)null);
        _rideRequests.GetRejectedByRouteIdAsync(ride.RouteId, default).Returns(new List<RideRequest>());

        await Handler().HandleAsync(new(passengerId, ride.Id), default);

        await _payments.Received(1).UnfreezeAsync(ride.FrozenPriceId!.Value, Arg.Any<CancellationToken>());
        await _payments.DidNotReceiveWithAnyArgs().ChargeCancellationAsync(default, default, default);
        await _events.Received(1).PublishAsync(
            "notification-triggers", Arg.Any<RideEndedEvent>(), Arg.Any<CancellationToken>());
        await _events.Received(1).PublishAsync(
            "ride-completions", Arg.Any<RideCancelledEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WaitingForDriver_ChargesCancellationFeeAndPublishesEvents()
    {
        var passengerId = Guid.NewGuid();
        var ride        = Make.WaitingForDriverRide(passengerId: passengerId);
        _rides.GetByIdAsync(ride.Id, default).Returns(ride);
        _routes.GetByIdAsync(ride.RouteId, default).Returns((Route?)null);
        _rideRequests.GetRejectedByRouteIdAsync(ride.RouteId, default).Returns(new List<RideRequest>());

        await Handler().HandleAsync(new(passengerId, ride.Id), default);

        await _payments.Received(1).ChargeCancellationAsync(
            ride.FrozenPriceId!.Value, ride.CancellationPrice, Arg.Any<CancellationToken>());
        await _payments.DidNotReceiveWithAnyArgs().UnfreezeAsync(default, default);
        await _events.Received(1).PublishAsync(
            "ride-completions", Arg.Any<RideCancelledEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_RideStarted_ThrowsInvalidRouteStatusException()
    {
        var passengerId = Guid.NewGuid();
        var ride        = Make.StartedRide(passengerId: passengerId);
        _rides.GetByIdAsync(ride.Id, default).Returns(ride);

        await Assert.ThrowsAsync<InvalidRouteStatusException>(() =>
            Handler().HandleAsync(new(passengerId, ride.Id), default));
    }

    [Fact]
    public async Task HandleAsync_WithRejectedPassengers_NotifiesThem()
    {
        var passengerId = Guid.NewGuid();
        var ride        = Make.WaitingForActivationRide(passengerId: passengerId);
        var rejected    = Make.PendingRequest(routeId: ride.RouteId);
        rejected.Reject();
        _rides.GetByIdAsync(ride.Id, default).Returns(ride);
        _routes.GetByIdAsync(ride.RouteId, default).Returns((Route?)null);
        _rideRequests.GetRejectedByRouteIdAsync(ride.RouteId, default)
                     .Returns(new List<RideRequest> { rejected });

        await Handler().HandleAsync(new(passengerId, ride.Id), default);

        await _events.Received(1).PublishAsync(
            "notification-triggers",
            Arg.Is<RideEndedEvent>(e => e.NotifyPassengerIds.Contains(rejected.PassengerId)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_RouteFound_CallsRemoveRide()
    {
        var passengerId = Guid.NewGuid();
        var route       = Make.ActiveRoute(capacity: 2);
        route.TryAddRide();
        var ride = Make.WaitingForActivationRide(routeId: route.Id, passengerId: passengerId);
        _rides.GetByIdAsync(ride.Id, default).Returns(ride);
        _routes.GetByIdAsync(ride.RouteId, default).Returns(route);
        _rideRequests.GetRejectedByRouteIdAsync(ride.RouteId, default).Returns(new List<RideRequest>());

        await Handler().HandleAsync(new(passengerId, ride.Id), default);

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
    public async Task HandleAsync_WrongPassenger_ThrowsUnauthorizedException()
    {
        var ride = Make.WaitingForActivationRide();
        _rides.GetByIdAsync(ride.Id, default).Returns(ride);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            Handler().HandleAsync(new(Guid.NewGuid(), ride.Id), default));
    }
}
