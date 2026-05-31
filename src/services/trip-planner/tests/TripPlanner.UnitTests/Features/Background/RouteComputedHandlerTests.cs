using TripPlanner.Application.Features.Background;

namespace TripPlanner.UnitTests.Features.Background;

public class RouteComputedHandlerTests
{
    private readonly IRouteJobRepository _jobs   = Substitute.For<IRouteJobRepository>();
    private readonly IRouteRepository    _routes = Substitute.For<IRouteRepository>();
    private readonly IEventPublisher     _events = Substitute.For<IEventPublisher>();
    private readonly IUnitOfWork         _uow    = Substitute.For<IUnitOfWork>();

    private RouteComputedHandler Handler() => new(_jobs, _routes, _events, new TripPlannerMetrics(), _uow, NullLogger<RouteComputedHandler>.Instance);

    private static BestRouteComputeResult BestRouteResult(Guid jobId) =>
        new(jobId, new BestRouteJobResult(
            [new GeoPoint(52.2, 21.0), new GeoPoint(52.3, 21.1)],
            DistanceMeters: 5000,
            DurationSeconds: 300));

    private static RideMatchingComputeResult RideMatchingResult(Guid jobId) =>
        new(jobId, new RideMatchingJobResult([]));

    private static FailedComputeResult FailedResult(Guid jobId) =>
        new(jobId, JobType.BestRoute, "osrm_timeout");

    // ─── BestRoute success ───────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_BestRoute_SetsGeometryAndPublishesRouteReadyEvent()
    {
        var driverId = Guid.NewGuid();
        var job      = Make.BestRouteJob(requesterId: driverId);
        var route    = Make.CalculatingRoute(driverId);
        _jobs.GetByCorrelationIdAsync(job.CorrelationId, default).Returns(job);
        _routes.GetActiveByDriverIdAsync(driverId, default).Returns(route);

        await Handler().HandleAsync(BestRouteResult(job.CorrelationId), default);

        Assert.Equal(RouteStatus.Created, route.Status);
        Assert.NotNull(route.RoutePoints);
        await _events.Received(1).PublishAsync(
            Topics.NotificationTriggers, Arg.Any<RouteReadyEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BestRoute_JobNotFound_ReturnsEarly()
    {
        _jobs.GetByCorrelationIdAsync(Arg.Any<Guid>(), default).Returns((RouteJob?)null);

        await Handler().HandleAsync(BestRouteResult(Guid.NewGuid()), default);

        await _routes.DidNotReceiveWithAnyArgs().GetActiveByDriverIdAsync(default, default);
        await _events.DidNotReceiveWithAnyArgs()
                     .PublishAsync(default!, Arg.Any<RouteReadyEvent>(), default);
    }

    [Fact]
    public async Task HandleAsync_BestRoute_RouteNotCalculating_ReturnsEarly()
    {
        var job   = Make.BestRouteJob();
        var route = Make.CreatedRoute(); // already Created, not Calculating
        _jobs.GetByCorrelationIdAsync(job.CorrelationId, default).Returns(job);
        _routes.GetActiveByDriverIdAsync(job.RequesterId, default).Returns(route);

        await Handler().HandleAsync(BestRouteResult(job.CorrelationId), default);

        await _events.DidNotReceiveWithAnyArgs()
                     .PublishAsync(default!, Arg.Any<RouteReadyEvent>(), default);
    }

    // ─── RideMatching success ─────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_RideMatching_MarksJobCompleteAndPublishesEvent()
    {
        var passengerId = Guid.NewGuid();
        var job         = Make.RideMatchingJob(requesterId: passengerId);
        _jobs.GetByCorrelationIdAsync(job.CorrelationId, default).Returns(job);

        await Handler().HandleAsync(RideMatchingResult(job.CorrelationId), default);

        Assert.Equal(JobStatus.Completed, job.Status);
        await _events.Received(1).PublishAsync(
            Topics.NotificationTriggers,
            Arg.Is<RouteSearchCompletedEvent>(e => e.PassengerId == passengerId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_RideMatching_JobNotFound_ReturnsEarly()
    {
        _jobs.GetByCorrelationIdAsync(Arg.Any<Guid>(), default).Returns((RouteJob?)null);

        await Handler().HandleAsync(RideMatchingResult(Guid.NewGuid()), default);

        await _events.DidNotReceiveWithAnyArgs()
                     .PublishAsync(default!, Arg.Any<RouteSearchCompletedEvent>(), default);
    }

    // ─── Failed ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Failed_MarksJobFailedWithError()
    {
        var job = Make.BestRouteJob();
        _jobs.GetByCorrelationIdAsync(job.CorrelationId, default).Returns(job);

        await Handler().HandleAsync(FailedResult(job.CorrelationId), default);

        Assert.Equal(JobStatus.Failed,    job.Status);
        Assert.Equal("osrm_timeout",      job.ErrorReason);
    }

    [Fact]
    public async Task HandleAsync_Failed_DoesNotPublishAnyEvents()
    {
        var job = Make.BestRouteJob();
        _jobs.GetByCorrelationIdAsync(job.CorrelationId, default).Returns(job);

        await Handler().HandleAsync(FailedResult(job.CorrelationId), default);

        await _events.DidNotReceiveWithAnyArgs()
                     .PublishAsync<IDomainEvent>(default!, default!, default);
    }

    [Fact]
    public async Task HandleAsync_Failed_JobNotFound_ReturnsEarly()
    {
        _jobs.GetByCorrelationIdAsync(Arg.Any<Guid>(), default).Returns((RouteJob?)null);

        await Handler().HandleAsync(FailedResult(Guid.NewGuid()), default);

        await _uow.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }
}