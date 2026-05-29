using TripPlanner.Application.Features.Driver;

namespace TripPlanner.UnitTests.Features.Driver;

public class CreateRouteHandlerTests
{
    private readonly IRouteRepository          _routes   = Substitute.For<IRouteRepository>();
    private readonly IRouteJobRepository       _jobs     = Substitute.For<IRouteJobRepository>();
    private readonly IComputePublisher<ComputeJob> _compute = Substitute.For<IComputePublisher<ComputeJob>>();
    private readonly IGeoService               _geo      = Substitute.For<IGeoService>();
    private readonly IAccountsService          _accounts = Substitute.For<IAccountsService>();
    private readonly IUnitOfWork               _uow      = Substitute.For<IUnitOfWork>();

    private CreateRouteHandler Handler() =>
        new(_routes, _jobs, _compute, _geo, _accounts, _uow);

    private CreateRouteCommand ValidCmd(Guid? driverId = null) =>
        new(driverId ?? Guid.NewGuid(), Make.PointA, Make.PointB, Capacity: 3);

    [Fact]
    public async Task HandleAsync_HappyPath_ReturnsRouteId()
    {
        var cmd = ValidCmd();
        _accounts.CanDriveAsync(cmd.DriverId, default).Returns(true);
        _geo.IsWithinServiceAreaAsync(Arg.Any<GeoPoint>(), default).Returns(true);

        var result = await Handler().HandleAsync(cmd, default);

        Assert.IsType<Guid>(result.RouteId);
        Assert.NotEqual(Guid.Empty, result.RouteId);
    }

    [Fact]
    public async Task HandleAsync_HappyPath_PersistsRouteAndJob()
    {
        var cmd = ValidCmd();
        _accounts.CanDriveAsync(cmd.DriverId, default).Returns(true);
        _geo.IsWithinServiceAreaAsync(Arg.Any<GeoPoint>(), default).Returns(true);

        await Handler().HandleAsync(cmd, default);

        await _routes.Received(1).AddAsync(Arg.Any<Route>(), Arg.Any<CancellationToken>());
        await _jobs.Received(1).AddAsync(Arg.Any<RouteJob>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_HappyPath_CommitsBeforePublishingToRabbit()
    {
        var cmd = ValidCmd();
        _accounts.CanDriveAsync(cmd.DriverId, default).Returns(true);
        _geo.IsWithinServiceAreaAsync(Arg.Any<GeoPoint>(), default).Returns(true);

        var callOrder = new List<string>();
        _uow.CommitAsync(default).Returns(_ => { callOrder.Add("commit"); return Task.CompletedTask; });
        _compute.PublishAsync(Arg.Any<ComputeJob>(), default)
                .Returns(_ => { callOrder.Add("publish"); return Task.CompletedTask; });

        await Handler().HandleAsync(cmd, default);

        Assert.Equal(["commit", "publish"], callOrder);
    }

    [Fact]
    public async Task HandleAsync_DriverNotAuthorised_ThrowsUnauthorizedException()
    {
        var cmd = ValidCmd();
        _accounts.CanDriveAsync(cmd.DriverId, default).Returns(false);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            Handler().HandleAsync(cmd, default));
    }

    [Fact]
    public async Task HandleAsync_StartOutsideServiceArea_ThrowsOutsideServiceAreaException()
    {
        var cmd = ValidCmd();
        _accounts.CanDriveAsync(cmd.DriverId, default).Returns(true);
        _geo.IsWithinServiceAreaAsync(Make.PointA, default).Returns(false);
        _geo.IsWithinServiceAreaAsync(Make.PointB, default).Returns(true);

        await Assert.ThrowsAsync<OutsideServiceAreaException>(() =>
            Handler().HandleAsync(cmd, default));
    }

    [Fact]
    public async Task HandleAsync_EndOutsideServiceArea_ThrowsOutsideServiceAreaException()
    {
        var cmd = ValidCmd();
        _accounts.CanDriveAsync(cmd.DriverId, default).Returns(true);
        _geo.IsWithinServiceAreaAsync(Make.PointA, default).Returns(true);
        _geo.IsWithinServiceAreaAsync(Make.PointB, default).Returns(false);

        await Assert.ThrowsAsync<OutsideServiceAreaException>(() =>
            Handler().HandleAsync(cmd, default));
    }
}
