using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Compute;

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
        // 2. Validate passenger's Start and End are within the active service area.
        // 3. Get a price quote and freeze funds atomically
        //    (IPaymentsService.QuoteAndFreezeAsync → PriceQuote).
        // 4. Create RideRequest; call rideRequest.SetFrozenPrice(quote.FrozenPriceId).
        // 5. Persist and commit.
        // 6. Publish RideRequestedEvent → notifications service will alert the driver.
        throw new NotImplementedException();
    }
}
