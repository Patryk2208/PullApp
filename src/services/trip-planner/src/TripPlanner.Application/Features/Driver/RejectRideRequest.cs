using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;

namespace TripPlanner.Application.Features.Driver;

public record RejectRideRequestCommand(Guid DriverId, Guid RequestId);

/// <summary>
/// Flow 4 — driver rejects a pending RideRequest.
/// </summary>
public class RejectRideRequestHandler(
    IRideRequestRepository rideRequests,
    IPaymentsService payments,
    IEventPublisher events,
    IUnitOfWork uow)
{
    public async Task HandleAsync(RejectRideRequestCommand cmd, CancellationToken ct)
    {
        // Flow 4
        // 1. Load RideRequest; verify it is Pending and belongs to the driver's route.
        // 2. Unfreeze the passenger's funds (IPaymentsService.UnfreezeAsync on FrozenPriceId).
        // 3. Call rideRequest.Reject().
        // 4. Persist and commit.
        // 5. Publish RideRejectedEvent → notifications service will alert the passenger.
        throw new NotImplementedException();
    }
}
