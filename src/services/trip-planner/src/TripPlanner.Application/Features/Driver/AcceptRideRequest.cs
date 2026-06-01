using Microsoft.Extensions.Logging;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Metrics;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Events;
using TripPlanner.Domain.Ride;
using TripPlanner.Domain.RideRequest;
using TripPlanner.Domain.Route;

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
    KafkaTopics topics,
    TripPlannerMetrics metrics,
    IUnitOfWork uow,
    ILogger<AcceptRideRequestHandler> logger)
{
    public async Task<AcceptRideRequestResult> HandleAsync(AcceptRideRequestCommand cmd, CancellationToken ct)
    {
        // Flow 5 — atomic section (steps 1-4 inside a single DB transaction)

        // 1a. Load RideRequest (must be Pending) — outside transaction, no locking needed here
        //     since a RideRequest can only be accepted by its route's driver.
        var request = await rideRequests.GetByIdAsync(cmd.RequestId, ct)
            ?? throw new RideRequestNotFoundException(cmd.RequestId);

        if (request.Status != RideRequestStatus.Pending)
            throw new InvalidRouteStatusException(
                $"RideRequest {cmd.RequestId} is no longer pending (current: {request.Status}).");

        // 1b. Open the transaction BEFORE reading the route so that the FOR UPDATE lock below
        //     serializes concurrent accepts targeting the same route. Without this, two concurrent
        //     calls could both read the same seat count in memory, both pass TryAddRide(), and both
        //     commit — exceeding the route capacity.
        await uow.BeginAsync(ct);

        Route route;
        Ride ride;
        bool routeBecameFull;

        try
        {
            // 1c. Lock the route row for the duration of the transaction.
            route = await routes.GetByIdForUpdateAsync(request.RouteId, ct)
                ?? throw new RouteNotFoundException(request.RouteId);

            if (route.DriverId != cmd.DriverId)
                throw new UnauthorizedException($"RideRequest {cmd.RequestId} belongs to a different driver's route.");

            if (route.Status == RouteStatus.Calculating)
                throw new InvalidRouteStatusException("Route geometry is still being computed.");

            // 1d. Call route.TryAddRide() → returns false if already Full.
            var wasActive = route.Status == RouteStatus.Active;
            if (!route.TryAddRide())
                throw new RouteFullException(route.Id);
            routeBecameFull = route.Status == RouteStatus.Full;

            // 2. Create Ride via Ride.Create(..., routeIsActive).
            ride = Ride.Create(
                route.Id, route.DriverId, request.PassengerId,
                request.StartPoint, request.EndPoint,
                request.Price, request.CancellationPrice, request.FrozenPriceId!.Value,
                routeIsActive: wasActive);

            // 3. Accept the RideRequest.
            request.Accept();

            // 4. Persist ride, updated route, updated rideRequest; commit.
            await rides.AddAsync(ride, ct);
            await routes.UpdateAsync(route, ct);
            await rideRequests.UpdateAsync(request, ct);
            await uow.CommitAsync(ct);
        }
        catch
        {
            await uow.RollbackAsync(ct);
            // Flow 5 fallback: transaction failed → treat as rejection, unfreeze passenger funds.
            if (request.FrozenPriceId.HasValue)
                await payments.UnfreezeAsync(request.FrozenPriceId.Value, CancellationToken.None);
            throw;
        }

        // Post-commit (transaction succeeded):

        // 5. If route became Full, reject all remaining pending requests.
        if (routeBecameFull)
        {
            var pendingRequests = await rideRequests.GetPendingByRouteIdAsync(route.Id, ct);
            foreach (var pending in pendingRequests)
            {
                if (pending.FrozenPriceId.HasValue)
                    await payments.UnfreezeAsync(pending.FrozenPriceId.Value, ct);
                pending.Reject();
                await rideRequests.UpdateAsync(pending, ct);
                await events.PublishAsync(topics.NotificationTriggers,
                    new RideRejectedEvent(pending.Id, route.Id, route.DriverId, pending.PassengerId), ct);
            }
        }

        // 6. Open a chat room; call ride.SetChatRoom(chatRoomId); persist.
        var chatRoomId = await chat.CreateRoomAsync(ride.Id, route.DriverId, request.PassengerId, ct);
        ride.SetChatRoom(chatRoomId);
        await rides.UpdateAsync(ride, ct);

        // 7. Publish RideAcceptedEvent → notifications service alerts the passenger.
        await events.PublishAsync(topics.NotificationTriggers,
            new RideAcceptedEvent(ride.Id, request.Id, route.Id, route.DriverId, request.PassengerId, chatRoomId), ct);

        metrics.RideTransition("pending_request", "ride_created", "driver_accepted");
        metrics.RideActiveAdd(1);
        logger.LogInformation("RideRequest accepted requestId={RequestId} rideId={RideId} routeId={RouteId} routeFull={RouteFull}",
            cmd.RequestId, ride.Id, route.Id, routeBecameFull);

        return new AcceptRideRequestResult(ride.Id, chatRoomId);
    }
}
