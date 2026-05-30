using TripPlanner.Application.Metrics;
using NSubstitute;
using TripPlanner.Application.Features.Background;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Events;
using TripPlanner.Domain;
using TripPlanner.Domain.Compute;
using TripPlanner.Domain.Route;
using TripPlanner.Infrastructure.Postgres;
using TripPlanner.IntegrationTests.Fixtures;

namespace TripPlanner.IntegrationTests.Handlers;

[Collection("Postgres")]
public class BackgroundHandlerIntegrationTests(PostgresFixture db) : IAsyncLifetime
{
    public Task InitializeAsync() => db.CleanAsync();
    public Task DisposeAsync()    => Task.CompletedTask;

    private static readonly GeoPoint PointA = new(52.2, 21.0);
    private static readonly GeoPoint PointB = new(52.3, 21.1);

    private async Task<Route?> LoadRoute(Guid id) =>
        await new PostgresRouteRepository(db.NewSession()).GetByIdAsync(id, default);

    private async Task<RouteJob?> LoadJob(Guid id) =>
        await new PostgresRouteJobRepository(db.NewSession()).GetByIdAsync(id, default);

    // ─── DriverRoute success ─────────────────────────────────────────────────

    [Fact]
    public async Task RouteComputed_DriverRoute_SetsGeometryOnRouteAndCompletesJob()
    {
        var driverId = Guid.NewGuid();
        var route    = Route.Create(driverId, PointA, PointB, capacity: 2);
        var corrId   = Guid.NewGuid();
        var job      = new RouteJob
        {
            Id            = Guid.NewGuid(),
            CorrelationId = corrId,
            JobType       = JobType.DriverRoute,
            RequesterId   = driverId,
            PayloadJson   = "{}",
            CreatedAt     = DateTimeOffset.UtcNow,
        };

        var seedSess = db.NewSession();
        await new PostgresRouteRepository(seedSess).AddAsync(route, default);
        await new PostgresRouteJobRepository(seedSess).AddAsync(job, default);

        var events  = Substitute.For<IEventPublisher>();
        var session = db.NewSession();
        var handler = new RouteComputedHandler(
            new PostgresRouteJobRepository(session),
            new PostgresRouteRepository(session),
            events, new TripPlannerMetrics(), session);

        await handler.HandleAsync(
            new DriverRouteComputeResult(corrId,
                new DriverRouteJobResult("{\"type\":\"LineString\"}", EtaSeconds: 600, DistanceMeters: 10000)),
            default);

        var loadedRoute = await LoadRoute(route.Id);
        var loadedJob   = await LoadJob(job.Id);

        Assert.Equal(RouteStatus.Created,        loadedRoute!.Status);
        Assert.Equal("{\"type\":\"LineString\"}", loadedRoute.GeometryJson);
        Assert.Equal(JobStatus.Completed,         loadedJob!.Status);
        Assert.NotNull(loadedJob.CompletedAt);
    }

    // ─── PassengerMatch success ───────────────────────────────────────────────

    [Fact]
    public async Task RouteComputed_PassengerMatch_CompletesJob()
    {
        var passengerId = Guid.NewGuid();
        var corrId      = Guid.NewGuid();
        var job         = new RouteJob
        {
            Id            = Guid.NewGuid(),
            CorrelationId = corrId,
            JobType       = JobType.PassengerMatch,
            RequesterId   = passengerId,
            PayloadJson   = "{}",
            CreatedAt     = DateTimeOffset.UtcNow,
        };

        var seedSess = db.NewSession();
        await new PostgresRouteJobRepository(seedSess).AddAsync(job, default);

        var events  = Substitute.For<IEventPublisher>();
        var session = db.NewSession();
        var handler = new RouteComputedHandler(
            new PostgresRouteJobRepository(session),
            new PostgresRouteRepository(session),
            events, new TripPlannerMetrics(), session);

        await handler.HandleAsync(
            new PassengerMatchComputeResult(corrId, new PassengerMatchJobResult([])),
            default);

        var loadedJob = await LoadJob(job.Id);
        Assert.Equal(JobStatus.Completed, loadedJob!.Status);
        await events.Received(1).PublishAsync(
            Arg.Any<string>(),
            Arg.Is<RouteSearchCompletedEvent>(e => e.PassengerId == passengerId),
            Arg.Any<CancellationToken>());
    }
}
