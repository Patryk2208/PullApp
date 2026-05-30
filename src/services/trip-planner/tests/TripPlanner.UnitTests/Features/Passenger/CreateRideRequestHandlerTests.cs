using TripPlanner.Application.Features.Passenger;

namespace TripPlanner.UnitTests.Features.Passenger;

public class CreateRideRequestHandlerTests
{
    private readonly IRouteRepository       _routes       = Substitute.For<IRouteRepository>();
    private readonly IRideRequestRepository _rideRequests = Substitute.For<IRideRequestRepository>();
    private readonly IPaymentsService       _payments     = Substitute.For<IPaymentsService>();
    private readonly IGeoService            _geo          = Substitute.For<IGeoService>();
    private readonly IEventPublisher        _events       = Substitute.For<IEventPublisher>();
    private readonly IUnitOfWork            _uow          = Substitute.For<IUnitOfWork>();

    private CreateRideRequestHandler Handler() =>
        new(_routes, _rideRequests, _payments, _geo, _events, _uow, NullLogger<CreateRideRequestHandler>.Instance);

    private void SetupHappyPath(Route route)
    {
        _routes.GetByIdAsync(route.Id, default).Returns(route);
        _geo.IsWithinServiceAreaAsync(Arg.Any<GeoPoint>(), default).Returns(true);
        _payments.QuoteAndFreezeAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<GeoPoint>(), Arg.Any<GeoPoint>(), default)
            .Returns(Make.Quote());
    }

    [Fact]
    public async Task HandleAsync_HappyPath_ReturnsRequestId()
    {
        var route = Make.CreatedRoute();
        SetupHappyPath(route);

        var result = await Handler().HandleAsync(
            new(Guid.NewGuid(), route.Id, Make.PointA, Make.PointB), default);

        Assert.NotEqual(Guid.Empty, result.RequestId);
    }

    [Fact]
    public async Task HandleAsync_HappyPath_PersistsRequestWithPriceAndPublishesEvent()
    {
        var route      = Make.CreatedRoute();
        var quote      = Make.Quote();
        _routes.GetByIdAsync(route.Id, default).Returns(route);
        _geo.IsWithinServiceAreaAsync(Arg.Any<GeoPoint>(), default).Returns(true);
        _payments.QuoteAndFreezeAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<GeoPoint>(), Arg.Any<GeoPoint>(), default)
            .Returns(quote);

        RideRequest? saved = null;
        await _rideRequests.AddAsync(Arg.Do<RideRequest>(r => saved = r), Arg.Any<CancellationToken>());

        await Handler().HandleAsync(
            new(Guid.NewGuid(), route.Id, Make.PointA, Make.PointB), default);

        Assert.NotNull(saved);
        Assert.Equal(quote.FrozenPriceId, saved!.FrozenPriceId);
        Assert.Equal(quote.Price,             saved.Price);
        Assert.Equal(quote.CancellationPrice, saved.CancellationPrice);

        await _events.Received(1).PublishAsync(
            Topics.NotificationTriggers, Arg.Any<RideRequestedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_RouteNotFound_ThrowsRouteNotFoundException()
    {
        _routes.GetByIdAsync(Arg.Any<Guid>(), default).Returns((Route?)null);

        await Assert.ThrowsAsync<RouteNotFoundException>(() =>
            Handler().HandleAsync(new(Guid.NewGuid(), Guid.NewGuid(), Make.PointA, Make.PointB), default));
    }

    [Fact]
    public async Task HandleAsync_RouteIsFull_ThrowsRouteFullException()
    {
        var route = Make.ActiveRoute(capacity: 1);
        route.TryAddRide(); // fills it → Full
        _routes.GetByIdAsync(route.Id, default).Returns(route);

        await Assert.ThrowsAsync<RouteFullException>(() =>
            Handler().HandleAsync(new(Guid.NewGuid(), route.Id, Make.PointA, Make.PointB), default));
    }

    [Fact]
    public async Task HandleAsync_PassengerStartOutsideArea_ThrowsOutsideServiceAreaException()
    {
        var route = Make.CreatedRoute();
        _routes.GetByIdAsync(route.Id, default).Returns(route);
        _geo.IsWithinServiceAreaAsync(Make.PointA, default).Returns(false);
        _geo.IsWithinServiceAreaAsync(Make.PointB, default).Returns(true);

        await Assert.ThrowsAsync<OutsideServiceAreaException>(() =>
            Handler().HandleAsync(new(Guid.NewGuid(), route.Id, Make.PointA, Make.PointB), default));
    }

    [Fact]
    public async Task HandleAsync_PassengerEndOutsideArea_ThrowsOutsideServiceAreaException()
    {
        var route = Make.CreatedRoute();
        _routes.GetByIdAsync(route.Id, default).Returns(route);
        _geo.IsWithinServiceAreaAsync(Make.PointA, default).Returns(true);
        _geo.IsWithinServiceAreaAsync(Make.PointB, default).Returns(false);

        await Assert.ThrowsAsync<OutsideServiceAreaException>(() =>
            Handler().HandleAsync(new(Guid.NewGuid(), route.Id, Make.PointA, Make.PointB), default));
    }
}
