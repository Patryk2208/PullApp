using System.Text.Json;
using Microsoft.Extensions.Logging;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Metrics;
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
    ISseHub sseHub,
    TripPlannerMetrics metrics,
    ILogger<SelectRouteHandler> logger)
{
    private static readonly TimeSpan ConfirmationWindow = TimeSpan.FromSeconds(30);

    public async Task HandleAsync(SelectRouteCommand cmd, CancellationToken ct)
    {
        logger.LogDebug("SelectRoute: passengerId={PassengerId} requestId={RequestId} driverRouteId={DriverRouteId}",
            cmd.PassengerId, cmd.RequestId, cmd.DriverRouteId);

        var request = await rideRequests.GetByIdAsync(cmd.RequestId, ct)
            ?? throw new NotFoundException("no_active_request");

        if (request.PassengerId != cmd.PassengerId) throw new ForbiddenException("forbidden");

        if (request.Status != RideRequestStatus.RoutesPresented)
        {
            logger.LogWarning("SelectRoute: invalid state {Status} for requestId={RequestId}", request.Status, cmd.RequestId);
            throw new InvalidStateTransitionException("invalid_state_transition");
        }

        var entry = request.MatchResults?.FirstOrDefault(m => m.DriverRouteId == cmd.DriverRouteId)
            ?? throw new RequestExpiredException();

        var driverRoute = await driverRoutes.GetByIdAsync(cmd.DriverRouteId, ct);
        if (driverRoute?.Status != Domain.Driver.DriverRouteStatus.Active)
        {
            logger.LogWarning("SelectRoute: driver route unavailable driverRouteId={DriverRouteId}", cmd.DriverRouteId);
            throw new DriverUnavailableException();
        }

        var deadline = DateTimeOffset.UtcNow.Add(ConfirmationWindow);
        request.SelectRoute(cmd.DriverRouteId, deadline);
        await rideRequests.UpdateAsync(request, ct);
        logger.LogDebug("SelectRoute: confirmation deadline={Deadline} requestId={RequestId}", deadline, cmd.RequestId);

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

        metrics.RouteSelected();
        logger.LogInformation("Passenger {PassengerId} selected route driverRouteId={DriverRouteId} requestId={RequestId}",
            cmd.PassengerId, cmd.DriverRouteId, cmd.RequestId);
    }
}
