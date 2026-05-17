using System.Net;
using System.Net.Http.Json;

namespace TripPlanner.UnitTests.E2E;

// E2E tests for passenger-side ride endpoints:
//   POST /api/passenger/rides/{rideId}/start         (PassengerStartRideHandler)
//   POST /api/passenger/rides/{rideId}/confirm-price (ConfirmPriceHandler)
//   POST /api/passenger/rides/{rideId}/cancel        (PassengerCancelRideHandler)
//
// Required externally:
//   - trip-planner service running on http://localhost:5238
//   - Postgres on localhost:5433 (trip-planner DB, service area seeded)
//   - RabbitMQ on localhost:5672
//
// Happy-path tests require a Ride record that can only be created after a full
// match flow (driver accepts confirmation). See flows/FullRideFlowE2ETests.
public class PassengerRideE2ETests
{
    private readonly HttpClient _client;

    public PassengerRideE2ETests()
    {
        _client = new HttpClient { BaseAddress = new Uri("http://localhost:5238") };
    }

    // ─── PassengerStartRide ───────────────────────────────────────────────────

    [Fact]
    public async Task PassengerStartRide_ShouldReturn404_WhenRideNotFound()
    {
        var passengerId = Guid.NewGuid().ToString();

        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/passenger/rides/{Guid.NewGuid()}/start");
        req.Headers.Add("X-User-Id", passengerId);

        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PassengerStartRide_ShouldReturn401_WhenMissingUserId()
    {
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/passenger/rides/{Guid.NewGuid()}/start");

        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ─── ConfirmPrice ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmPrice_ShouldReturn404_WhenRideNotFound()
    {
        var passengerId = Guid.NewGuid().ToString();

        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/passenger/rides/{Guid.NewGuid()}/confirm-price");
        req.Headers.Add("X-User-Id", passengerId);

        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ─── PassengerCancelRide ──────────────────────────────────────────────────

    [Fact]
    public async Task PassengerCancelRide_ShouldReturn404_WhenRideNotFound()
    {
        var passengerId = Guid.NewGuid().ToString();

        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/passenger/rides/{Guid.NewGuid()}/cancel");
        req.Headers.Add("X-User-Id", passengerId);
        req.Content = JsonContent.Create(new { reason = (string?)null });

        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PassengerCancelRide_ShouldReturn401_WhenMissingUserId()
    {
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/passenger/rides/{Guid.NewGuid()}/cancel");
        req.Content = JsonContent.Create(new { reason = (string?)null });

        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
