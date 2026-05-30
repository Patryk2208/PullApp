using TripPlanner.Application.Services;
using TripPlanner.Domain.Compute;

namespace TripPlanner.Infrastructure.Fakes;

public class FakePaymentsService : IPaymentsService
{
    public Task<PriceQuote> QuoteAndFreezeAsync(
        Guid routeId, Guid passengerId, GeoPoint startPoint, GeoPoint endPoint, CancellationToken ct)
        => Task.FromResult(new PriceQuote(
            FrozenPriceId: Guid.NewGuid(),
            Price: 25.50m,
            CancellationPrice: 5.00m,
            Currency: "PLN",
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(10)));

    public Task UnfreezeAsync(Guid frozenPriceId, CancellationToken ct)
        => Task.CompletedTask;

    public Task ChargeAsync(Guid frozenPriceId, CancellationToken ct)
        => Task.CompletedTask;

    public Task ChargeCancellationAsync(Guid frozenPriceId, decimal cancellationPrice, CancellationToken ct)
        => Task.CompletedTask;
}
