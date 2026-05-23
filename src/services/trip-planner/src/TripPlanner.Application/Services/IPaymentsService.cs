using TripPlanner.Domain.Compute;

namespace TripPlanner.Application.Services;

public record PriceQuote(
    Guid FrozenPriceId,
    decimal Amount,
    string Currency,
    DateTimeOffset ExpiresAt);

public interface IPaymentsService
{
    Task<PriceQuote> QuotePriceAsync(
        Guid driverRouteId,
        Guid passengerId,
        GeoPoint pickupPoint,
        GeoPoint dropoffPoint,
        int distanceMeters,
        int etaSeconds,
        CancellationToken ct);
}
