using TripPlanner.Application.Features.Passenger;

namespace TripPlanner.UnitTests.Features.Passenger;

public class SubmitRouteSearchHandlerTests
{
    private readonly IRouteJobRepository       _jobs    = Substitute.For<IRouteJobRepository>();
    private readonly IComputePublisher<ComputeJob> _compute = Substitute.For<IComputePublisher<ComputeJob>>();
    private readonly IGeoService               _geo     = Substitute.For<IGeoService>();
    private readonly IUnitOfWork               _uow     = Substitute.For<IUnitOfWork>();

    private SubmitRouteSearchHandler Handler() => new(_jobs, _compute, _geo, new TripPlannerMetrics(), _uow, NullLogger<SubmitRouteSearchHandler>.Instance);

    private SubmitRouteSearchCommand ValidCmd(Guid? passengerId = null) =>
        new(passengerId ?? Guid.NewGuid(), Make.PointA, Make.PointB);

    [Fact]
    public async Task HandleAsync_HappyPath_ReturnsJobId()
    {
        var cmd = ValidCmd();
        _geo.IsWithinServiceAreaAsync(Arg.Any<GeoPoint>(), default).Returns(true);

        var result = await Handler().HandleAsync(cmd, default);

        Assert.NotEqual(Guid.Empty, result.JobId);
    }

    [Fact]
    public async Task HandleAsync_HappyPath_PersistsJobAndCommitsBeforePublish()
    {
        var cmd      = ValidCmd();
        _geo.IsWithinServiceAreaAsync(Arg.Any<GeoPoint>(), default).Returns(true);
        var callOrder = new List<string>();
        _uow.CommitAsync(default).Returns(_ => { callOrder.Add("commit"); return Task.CompletedTask; });
        _compute.PublishAsync(Arg.Any<ComputeJob>(), default)
                .Returns(_ => { callOrder.Add("publish"); return Task.CompletedTask; });

        await Handler().HandleAsync(cmd, default);

        await _jobs.Received(1).AddAsync(Arg.Any<RouteJob>(), Arg.Any<CancellationToken>());
        Assert.Equal(["commit", "publish"], callOrder);
    }

    [Fact]
    public async Task HandleAsync_StartOutsideServiceArea_ThrowsOutsideServiceAreaException()
    {
        var cmd = ValidCmd();
        _geo.IsWithinServiceAreaAsync(Make.PointA, default).Returns(false);
        _geo.IsWithinServiceAreaAsync(Make.PointB, default).Returns(true);

        await Assert.ThrowsAsync<OutsideServiceAreaException>(() =>
            Handler().HandleAsync(cmd, default));
    }

    [Fact]
    public async Task HandleAsync_EndOutsideServiceArea_ThrowsOutsideServiceAreaException()
    {
        var cmd = ValidCmd();
        _geo.IsWithinServiceAreaAsync(Make.PointA, default).Returns(true);
        _geo.IsWithinServiceAreaAsync(Make.PointB, default).Returns(false);

        await Assert.ThrowsAsync<OutsideServiceAreaException>(() =>
            Handler().HandleAsync(cmd, default));
    }
}
