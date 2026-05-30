using TripPlanner.Domain.Compute;

namespace TripPlanner.Application.Services;

public record PriceQuote(
    Guid FrozenPriceId,
    decimal Price,
    decimal CancellationPrice,
    string Currency,
    DateTimeOffset ExpiresAt);

public interface IPaymentsService
{
    // Flow 3: calculate price for the ride and freeze the funds atomically.
    Task<PriceQuote> QuoteAndFreezeAsync(Guid routeId, Guid passengerId, GeoPoint startPoint, GeoPoint endPoint, CancellationToken ct);

    // Flow 4, 8a: release frozen funds.
    Task UnfreezeAsync(Guid frozenPriceId, CancellationToken ct);

    // Flow 8c happy path: charge the full ride price.
    Task ChargeAsync(Guid frozenPriceId, CancellationToken ct);

    // Flow 8b: charge only the cancellation fee; remainder is released.
    Task ChargeCancellationAsync(Guid frozenPriceId, decimal cancellationPrice, CancellationToken ct);
}
