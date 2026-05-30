using TripPlanner.Application.Features.Background;

namespace TripPlanner.UnitTests.Features.Background;

public class RouteComputedHandlerTests
{
    private readonly IRouteJobRepository _jobs   = Substitute.For<IRouteJobRepository>();
    private readonly IRouteRepository    _routes = Substitute.For<IRouteRepository>();
    private readonly IEventPublisher     _events = Substitute.For<IEventPublisher>();
    private readonly IUnitOfWork         _uow    = Substitute.For<IUnitOfWork>();

    private RouteComputedHandler Handler() => new(_jobs, _routes, _events, new TripPlannerMetrics(), _uow);

    private static DriverRouteComputeResult DriverRouteResult(Guid jobId) =>
        new(jobId, new DriverRouteJobResult("{}", EtaSeconds: 300, DistanceMeters: 5000));

    private static PassengerMatchComputeResult PassengerMatchResult(Guid jobId) =>
        new(jobId, new PassengerMatchJobResult([]));

    private static FailedComputeResult FailedResult(Guid jobId) =>
        new(jobId, JobType.DriverRoute, "osrm_timeout");

    // ─── DriverRoute success ─────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_DriverRoute_SetsGeometryAndPublishesRouteReadyEvent()
    {
        var driverId = Guid.NewGuid();
        var job      = Make.DriverRouteJob(requesterId: driverId);
        var route    = Make.CalculatingRoute(driverId);
        _jobs.GetByCorrelationIdAsync(job.CorrelationId, default).Returns(job);
        _routes.GetActiveByDriverIdAsync(driverId, default).Returns(route);

        await Handler().HandleAsync(DriverRouteResult(job.CorrelationId), default);

        Assert.Equal(RouteStatus.Created, route.Status);
        Assert.Equal("{}", route.GeometryJson);
        await _events.Received(1).PublishAsync(
            Topics.NotificationTriggers, Arg.Any<RouteReadyEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_DriverRoute_JobNotFound_ReturnsEarly()
    {
        _jobs.GetByCorrelationIdAsync(Arg.Any<Guid>(), default).Returns((RouteJob?)null);

        await Handler().HandleAsync(DriverRouteResult(Guid.NewGuid()), default);

        await _routes.DidNotReceiveWithAnyArgs().GetActiveByDriverIdAsync(default, default);
        await _events.DidNotReceiveWithAnyArgs()
                     .PublishAsync(default!, Arg.Any<RouteReadyEvent>(), default);
    }

    [Fact]
    public async Task HandleAsync_DriverRoute_RouteNotCalculating_ReturnsEarly()
    {
        var job   = Make.DriverRouteJob();
        var route = Make.CreatedRoute(); // already Created, not Calculating
        _jobs.GetByCorrelationIdAsync(job.CorrelationId, default).Returns(job);
        _routes.GetActiveByDriverIdAsync(job.RequesterId, default).Returns(route);

        await Handler().HandleAsync(DriverRouteResult(job.CorrelationId), default);

        await _events.DidNotReceiveWithAnyArgs()
                     .PublishAsync(default!, Arg.Any<RouteReadyEvent>(), default);
    }

    // ─── PassengerMatch success ───────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_PassengerMatch_MarksJobCompleteAndPublishesEvent()
    {
        var passengerId = Guid.NewGuid();
        var job         = Make.PassengerMatchJob(requesterId: passengerId);
        _jobs.GetByCorrelationIdAsync(job.CorrelationId, default).Returns(job);

        await Handler().HandleAsync(PassengerMatchResult(job.CorrelationId), default);

        Assert.Equal(JobStatus.Completed, job.Status);
        await _events.Received(1).PublishAsync(
            Topics.NotificationTriggers,
            Arg.Is<RouteSearchCompletedEvent>(e => e.PassengerId == passengerId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PassengerMatch_JobNotFound_ReturnsEarly()
    {
        _jobs.GetByCorrelationIdAsync(Arg.Any<Guid>(), default).Returns((RouteJob?)null);

        await Handler().HandleAsync(PassengerMatchResult(Guid.NewGuid()), default);

        await _events.DidNotReceiveWithAnyArgs()
                     .PublishAsync(default!, Arg.Any<RouteSearchCompletedEvent>(), default);
    }

    // ─── Failed ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Failed_MarksJobFailedWithError()
    {
        var job = Make.DriverRouteJob();
        _jobs.GetByCorrelationIdAsync(job.CorrelationId, default).Returns(job);

        await Handler().HandleAsync(FailedResult(job.CorrelationId), default);

        Assert.Equal(JobStatus.Failed,    job.Status);
        Assert.Equal("osrm_timeout",      job.ErrorReason);
    }

    [Fact]
    public async Task HandleAsync_Failed_DoesNotPublishAnyEvents()
    {
        var job = Make.DriverRouteJob();
        _jobs.GetByCorrelationIdAsync(job.CorrelationId, default).Returns(job);

        await Handler().HandleAsync(FailedResult(job.CorrelationId), default);

        await _events.DidNotReceiveWithAnyArgs()
                     .PublishAsync<IDomainEvent>(default!, default!, default);
    }

    [Fact]
    public async Task HandleAsync_Failed_JobNotFound_ReturnsEarly()
    {
        _jobs.GetByCorrelationIdAsync(Arg.Any<Guid>(), default).Returns((RouteJob?)null);

        // Should not throw
        await Handler().HandleAsync(FailedResult(Guid.NewGuid()), default);

        await _uow.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }
}
