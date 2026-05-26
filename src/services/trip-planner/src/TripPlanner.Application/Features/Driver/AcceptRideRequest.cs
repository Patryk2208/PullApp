using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;

namespace TripPlanner.Application.Features.Driver;

public record AcceptRideRequestCommand(Guid DriverId, Guid RequestId);
public record AcceptRideRequestResult(Guid RideId, Guid? ChatRoomId);

/// <summary>
/// Flow 5 — driver accepts a RideRequest. Everything up to step 3 is atomic.
/// </summary>
public class AcceptRideRequestHandler(
    IRouteRepository routes,
    IRideRequestRepository rideRequests,
    IRideRepository rides,
    IPaymentsService payments,
    IChatService chat,
    IEventPublisher events,
    IUnitOfWork uow)
{
    public async Task<AcceptRideRequestResult> HandleAsync(AcceptRideRequestCommand cmd, CancellationToken ct)
    {
        // Flow 5 — atomic section (steps 1-3 inside a single DB transaction)
        // 1a. Load RideRequest (must be Pending); load Route (must not be Full).
        // 1b. Call route.TryAddRide() → if returns false, throw RouteFullException.
        //     If the route is now Full after adding, remaining pending requests will be
        //     rejected in step 5 (post-commit).
        // 2.  Create Ride via Ride.Create(..., routeIsActive: route.Status == Active).
        // 3.  Accept the RideRequest (rideRequest.Accept()).
        // 4.  Persist ride, updated route, updated rideRequest; commit.
        //
        // Post-commit (if transaction succeeded):
        // 5.  If route became Full, load all remaining Pending RideRequests for this route
        //     and reject each one (flow 4 logic: unfreeze funds + publish RideRejectedEvent).
        // 6.  Open a chat room (IChatService.CreateRoomAsync); call ride.SetChatRoom(chatRoomId).
        //     Persist the updated ride.
        // 7.  Publish RideAcceptedEvent → notifications service alerts the passenger.
        //
        // If the transaction in step 4 fails:
        // 8.  Unfreeze passenger's frozen funds and reject the RideRequest (flow 4 fallback).
        throw new NotImplementedException();
    }
}
