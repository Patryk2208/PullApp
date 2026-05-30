using TripPlanner.Application.Features.Driver;

namespace TripPlanner.UnitTests.Features.Driver;

public class ActivateRouteHandlerTests
{
    private readonly IRouteRepository _routes = Substitute.For<IRouteRepository>();
    private readonly IRideRepository  _rides  = Substitute.For<IRideRepository>();
    private readonly IGeoService      _geo    = Substitute.For<IGeoService>();
    private readonly IUnitOfWork      _uow    = Substitute.For<IUnitOfWork>();

    private ActivateRouteHandler Handler() => new(_routes, _rides, _geo, _uow);

    [Fact]
    public async Task HandleAsync_HappyPath_ActivatesRoute()
    {
        var driverId = Guid.NewGuid();
        var route    = Make.CreatedRoute(driverId);
        _routes.GetByIdAsync(route.Id, default).Returns(route);
        _rides.GetActiveByRouteIdAsync(route.Id, default).Returns(new List<Ride>());
        _geo.IsNearAsync(Arg.Any<GeoPoint>(), Arg.Any<GeoPoint>(), Arg.Any<double>(), default).Returns(true);

        await Handler().HandleAsync(new(driverId, route.Id, Make.PointA), default);

        Assert.Equal(RouteStatus.Active, route.Status);
        await _routes.Received(1).UpdateAsync(route, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithWaitingRides_TransitionsThemToWaitingForDriver()
    {
        var driverId = Guid.NewGuid();
        var route    = Make.CreatedRoute(driverId);
        var ride     = Make.WaitingForActivationRide(routeId: route.Id, driverId: driverId);
        _routes.GetByIdAsync(route.Id, default).Returns(route);
        _rides.GetActiveByRouteIdAsync(route.Id, default).Returns(new List<Ride> { ride });
        _geo.IsNearAsync(Arg.Any<GeoPoint>(), Arg.Any<GeoPoint>(), Arg.Any<double>(), default).Returns(true);

        await Handler().HandleAsync(new(driverId, route.Id, Make.PointA), default);

        Assert.Equal(RideStatus.WaitingForDriver, ride.Status);
        await _rides.Received(1).UpdateAsync(ride, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_RouteNotFound_ThrowsRouteNotFoundException()
    {
        _routes.GetByIdAsync(Arg.Any<Guid>(), default).Returns((Route?)null);

        await Assert.ThrowsAsync<RouteNotFoundException>(() =>
            Handler().HandleAsync(new(Guid.NewGuid(), Guid.NewGuid(), Make.PointA), default));
    }

    [Fact]
    public async Task HandleAsync_WrongDriver_ThrowsUnauthorizedException()
    {
        var route = Make.CreatedRoute();
        _routes.GetByIdAsync(route.Id, default).Returns(route);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            Handler().HandleAsync(new(Guid.NewGuid(), route.Id, Make.PointA), default));
    }

    [Fact]
    public async Task HandleAsync_RouteNotCreatedStatus_ThrowsInvalidRouteStatusException()
    {
        var driverId = Guid.NewGuid();
        var route    = Make.CalculatingRoute(driverId); // Status == Calculating
        _routes.GetByIdAsync(route.Id, default).Returns(route);

        await Assert.ThrowsAsync<InvalidRouteStatusException>(() =>
            Handler().HandleAsync(new(driverId, route.Id, Make.PointA), default));
    }

    [Fact]
    public async Task HandleAsync_DriverTooFarFromStart_ThrowsOutsideServiceAreaException()
    {
        var driverId = Guid.NewGuid();
        var route    = Make.CreatedRoute(driverId);
        _routes.GetByIdAsync(route.Id, default).Returns(route);
        _geo.IsNearAsync(Arg.Any<GeoPoint>(), Arg.Any<GeoPoint>(), Arg.Any<double>(), default).Returns(false);

        await Assert.ThrowsAsync<OutsideServiceAreaException>(() =>
            Handler().HandleAsync(new(driverId, route.Id, Make.PointA), default));
    }
}
