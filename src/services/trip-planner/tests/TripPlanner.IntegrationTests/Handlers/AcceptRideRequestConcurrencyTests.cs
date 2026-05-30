using TripPlanner.Application.Metrics;
using NSubstitute;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Features.Driver;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Compute;
using TripPlanner.Domain.RideRequest;
using TripPlanner.Domain.Route;
using TripPlanner.Infrastructure.Postgres;
using TripPlanner.IntegrationTests.Fixtures;

namespace TripPlanner.IntegrationTests.Handlers;

[Collection("Postgres")]
public class AcceptRideRequestConcurrencyTests(PostgresFixture db) : IAsyncLifetime
{
    public Task InitializeAsync() => db.CleanAsync();
    public Task DisposeAsync()    => Task.CompletedTask;

    private static readonly GeoPoint PointA = new(52.2, 21.0);
    private static readonly GeoPoint PointB = new(52.3, 21.1);

    private AcceptRideRequestHandler BuildHandler()
    {
        var session  = db.NewSession();
        var payments = Substitute.For<IPaymentsService>();
        var chat     = Substitute.For<IChatService>();
        var events   = Substitute.For<IEventPublisher>();
        chat.CreateRoomAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), default)
            .Returns(Guid.NewGuid());
        return new AcceptRideRequestHandler(
            new PostgresRouteRepository(session),
            new PostgresRideRequestRepository(session),
            new PostgresRideRepository(session),
            payments, chat, events, new TripPlannerMetrics(), session);
    }

    /// <summary>
    /// Two concurrent accepts on a capacity-1 route: SELECT FOR UPDATE ensures only one
    /// succeeds. The second reads the post-commit Full status and throws RouteFullException.
    /// </summary>
    [Fact]
    public async Task ConcurrentAccepts_OnCapacityOneRoute_OnlyOneSucceeds()
    {
        var driverId = Guid.NewGuid();
        var route    = Route.Create(driverId, PointA, PointB, capacity: 1);
        route.SetGeometry("{}", 300, 5000);
        route.Activate(PointA);

        var req1 = RideRequest.Create(route.Id, Guid.NewGuid(), PointA, PointB);
        req1.SetFrozenPrice(Guid.NewGuid(), 20m, 4m);
        var req2 = RideRequest.Create(route.Id, Guid.NewGuid(), PointA, PointB);
        req2.SetFrozenPrice(Guid.NewGuid(), 20m, 4m);

        var seed = db.NewSession();
        await new PostgresRouteRepository(seed).AddAsync(route, default);
        await new PostgresRouteRepository(seed).UpdateAsync(route, default);
        await new PostgresRideRequestRepository(seed).AddAsync(req1, default);
        await new PostgresRideRequestRepository(seed).AddAsync(req2, default);

        // Run both accepts concurrently — each has its own DbSession/connection.
        var t1 = TryAccept(driverId, req1.Id);
        var t2 = TryAccept(driverId, req2.Id);
        var results = await Task.WhenAll(t1, t2);

        Assert.Equal(1, results.Count(r => r));   // exactly one accepted
        Assert.Equal(1, results.Count(r => !r));  // exactly one rejected with RouteFullException

        // Verify only one ride was written to the DB.
        var rides = await new PostgresRideRepository(db.NewSession())
                              .GetActiveByRouteIdAsync(route.Id, default);
        Assert.Single(rides);
    }

    private async Task<bool> TryAccept(Guid driverId, Guid requestId)
    {
        try
        {
            await BuildHandler().HandleAsync(new(driverId, requestId), default);
            return true;
        }
        catch (RouteFullException)
        {
            return false;
        }
    }
}
