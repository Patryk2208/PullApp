using TripPlanner.Application.Services;
using TripPlanner.Domain.Compute;

namespace TripPlanner.Infrastructure.Fakes;

public class FakePaymentsService : IPaymentsService
{
    public Task<PriceQuote> QuotePriceAsync(
        Guid driverRouteId,
        Guid passengerId,
        GeoPoint pickupPoint,
        GeoPoint dropoffPoint,
        int distanceMeters,
        int etaSeconds,
        CancellationToken ct)
        => Task.FromResult(new PriceQuote(
            Guid.NewGuid(),
            Amount: 25.50m,
            Currency: "PLN",
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(10)));
}
