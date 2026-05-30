namespace TripPlanner.Application.Exceptions;

public class DomainException(string message) : Exception(message);

public class RouteNotFoundException(Guid id) : DomainException($"Route {id} not found.");
public class RouteFullException(Guid id) : DomainException($"Route {id} is full.");
public class RouteNotDeletableException(Guid id) : DomainException($"Route {id} has active rides and cannot be deleted.");
public class RideNotFoundException(Guid id) : DomainException($"Ride {id} not found.");
public class RideRequestNotFoundException(Guid id) : DomainException($"RideRequest {id} not found.");
public class InvalidRouteStatusException(string message) : DomainException(message);
public class UnauthorizedException(string message) : DomainException(message);
// Declaration made out of order (e.g. passenger declares pickup before driver has).
public class DeclarationOrderException(string message) : DomainException(message);
public class OutsideServiceAreaException(string message) : DomainException(message);
public class DownstreamUnavailableException(string service, Exception inner)
    : Exception($"{service} unavailable.", inner);
