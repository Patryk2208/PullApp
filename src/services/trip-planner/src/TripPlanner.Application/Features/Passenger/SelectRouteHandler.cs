using System.Text.Json;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Events;
using TripPlanner.Domain.Passenger;
using TripPlanner.Application.Features.DTO.Sse;

namespace TripPlanner.Application.Features.Passenger;

public record SelectRouteCommand(Guid PassengerId, Guid RequestId, Guid DriverRouteId);

public class SelectRouteHandler(
    IRideRequestRepository rideRequests,
    IDriverRouteRepository driverRoutes,
    IEventPublisher @event,
    ISseHub sseHub)
{
    private static readonly TimeSpan ConfirmationWindow = TimeSpan.FromSeconds(30);

    public async Task HandleAsync(SelectRouteCommand cmd, CancellationToken ct)
    {
        var request = await rideRequests.GetByIdAsync(cmd.RequestId, ct)
            ?? throw new NotFoundException("no_active_request");

        if (request.PassengerId != cmd.PassengerId) throw new ForbiddenException("forbidden");

        if (request.Status != RideRequestStatus.RoutesPresented)
            throw new InvalidStateTransitionException("invalid_state_transition");

        var entry = request.MatchResults?.FirstOrDefault(m => m.DriverRouteId == cmd.DriverRouteId)
            ?? throw new RequestExpiredException();

        var driverRoute = await driverRoutes.GetByIdAsync(cmd.DriverRouteId, ct);
        if (driverRoute?.Status != Domain.Driver.DriverRouteStatus.Active)
            throw new DriverUnavailableException();

        var deadline = DateTimeOffset.UtcNow.Add(ConfirmationWindow);
        request.SelectRoute(cmd.DriverRouteId, deadline);
        await rideRequests.UpdateAsync(request, ct);

        var driverEntry = request.MatchResults!.First(m => m.DriverRouteId == cmd.DriverRouteId);

        await @event.PublishAsync(Topics.UserActions,
            new RouteSelectedEvent(
                cmd.RequestId,
                driverEntry.DriverId,
                cmd.PassengerId,
                "Jan Kowalski", // TODO: fetch display name from Accounts
                request.StartPoint,
                request.EndPoint,
                driverEntry.EtaToPassengerSeconds,
                deadline),
            ct);

        await sseHub.PushAsync(cmd.RequestId, "awaiting_driver",
            JsonSerializer.Serialize(new AwaitingDriverEvent(cmd.RequestId, deadline)), ct);
    }
}
