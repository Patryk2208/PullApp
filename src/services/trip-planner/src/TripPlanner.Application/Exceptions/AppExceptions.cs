namespace TripPlanner.Application.Exceptions;

// ─── 400 ──────────────────────────────────────────────────────────────────────

public class BadRequestException(string message) : Exception(message);

// ─── 404 ──────────────────────────────────────────────────────────────────────

public class NotFoundException(string message) : Exception(message);

// ─── 403 ──────────────────────────────────────────────────────────────────────

public class ForbiddenException(string message) : Exception(message);

// ─── 409 ──────────────────────────────────────────────────────────────────────

public class RouteAlreadyActiveException()
    : Exception("route_already_active");

public class CannotModifyDuringRideException()
    : Exception("cannot_modify_during_ride");

public class InvalidStateTransitionException(string message)
    : Exception(message);

public class RequestExpiredException()
    : Exception("request_expired");

public class DriverUnavailableException()
    : Exception("driver_unavailable");

// ─── 422 ──────────────────────────────────────────────────────────────────────

public class OutsideServiceAreaException()
    : Exception("outside_service_area");

// ─── 503 ──────────────────────────────────────────────────────────────────────

public class AccountsUnavailableException()
    : Exception("accounts_unavailable");

public class PaymentsUnavailableException()
    : Exception("payments_unavailable");

public class ChatUnavailableException()
    : Exception("chat_unavailable");
