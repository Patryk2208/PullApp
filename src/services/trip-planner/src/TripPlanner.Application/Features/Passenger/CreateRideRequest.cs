using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Compute;
using TripPlanner.Domain.Events;
using TripPlanner.Domain.RideRequest;
using TripPlanner.Domain.Route;

namespace TripPlanner.Application.Features.Passenger;

public record CreateRideRequestCommand(Guid PassengerId, Guid RouteId, GeoPoint Start, GeoPoint End);
public record CreateRideRequestResult(Guid RequestId);

/// <summary>
/// Flow 3 — passenger picks a route and creates a RideRequest.
/// </summary>
public class CreateRideRequestHandler(
    IRouteRepository routes,
    IRideRequestRepository rideRequests,
    IPaymentsService payments,
    IGeoService geo,
    IEventPublisher events,
    IUnitOfWork uow)
{
    public async Task<CreateRideRequestResult> HandleAsync(CreateRideRequestCommand cmd, CancellationToken ct)
    {
        // Flow 3
        // 1. Load Route; verify Status != Full (throw RouteFullException if full).
        var route = await routes.GetByIdAsync(cmd.RouteId, ct)
            ?? throw new RouteNotFoundException(cmd.RouteId);

        if (route.Status == RouteStatus.Full)
            throw new RouteFullException(route.Id);

        // 2. Validate passenger's Start and End are within the active service area.
        if (!await geo.IsWithinServiceAreaAsync(cmd.Start, ct) || !await geo.IsWithinServiceAreaAsync(cmd.End, ct))
            throw new OutsideServiceAreaException("Passenger start or end is outside the active service area.");

        // 3. Get a price quote and freeze funds atomically
        //    (IPaymentsService.QuoteAndFreezeAsync → PriceQuote).
        var quote = await payments.QuoteAndFreezeAsync(route.Id, cmd.PassengerId, cmd.Start, cmd.End, ct);

        // 4. Create RideRequest; call rideRequest.SetFrozenPrice(quote.*).
        var request = RideRequest.Create(cmd.RouteId, cmd.PassengerId, cmd.Start, cmd.End);
        request.SetFrozenPrice(quote.FrozenPriceId, quote.Price, quote.CancellationPrice);

        // 5. Persist and commit.
        await rideRequests.AddAsync(request, ct);
        await uow.CommitAsync(ct);

        // 6. Publish RideRequestedEvent → notifications service will alert the driver.
        await events.PublishAsync(Topics.NotificationTriggers,
            new RideRequestedEvent(request.Id, route.Id, route.DriverId, cmd.PassengerId, cmd.Start, cmd.End), ct);

        return new CreateRideRequestResult(request.Id);
    }
}
