using System.Net;
using System.Net.Http.Json;

namespace TripPlanner.UnitTests.E2E;

// E2E tests for driver-side ride lifecycle endpoints:
//   POST /api/driver/rides/{rideId}/arrived   (DriverArrivedHandler)
//   POST /api/driver/rides/{rideId}/start     (DriverStartRideHandler)
//   POST /api/driver/rides/{rideId}/complete  (CompleteRideHandler)
//   POST /api/driver/rides/{rideId}/cancel    (DriverCancelRideHandler)
//
// Required externally:
//   - trip-planner service running on http://localhost:5238
//   - Postgres on localhost:5433 (trip-planner DB, service area seeded)
//   - RabbitMQ on localhost:5672
//
// Happy-path tests (with a real Ride in Pickup/AwaitingPassenger/InRide state)
// require the full match flow to have completed first (driver confirmed the match).
// Full lifecycle is covered by flows/FullRideFlowE2ETests.
public class DriverRideLifecycleE2ETests
{
    private readonly HttpClient _client;

    public DriverRideLifecycleE2ETests()
    {
        _client = new HttpClient { BaseAddress = new Uri("http://localhost:5238") };
    }

    // ─── DriverArrived ────────────────────────────────────────────────────────

    [Fact]
    public async Task DriverArrived_ShouldReturn404_WhenRideNotFound()
    {
        var driverId = Guid.NewGuid().ToString();
        var unknownRideId = Guid.NewGuid();

        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/driver/rides/{unknownRideId}/arrived");
        req.Headers.Add("X-User-Id", driverId);

        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DriverArrived_ShouldReturn401_WhenMissingUserId()
    {
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/driver/rides/{Guid.NewGuid()}/arrived");

        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ─── DriverStartRide ──────────────────────────────────────────────────────

    [Fact]
    public async Task DriverStartRide_ShouldReturn404_WhenRideNotFound()
    {
        var driverId = Guid.NewGuid().ToString();

        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/driver/rides/{Guid.NewGuid()}/start");
        req.Headers.Add("X-User-Id", driverId);

        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ─── CompleteRide ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteRide_ShouldReturn404_WhenRideNotFound()
    {
        var driverId = Guid.NewGuid().ToString();

        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/driver/rides/{Guid.NewGuid()}/complete");
        req.Headers.Add("X-User-Id", driverId);
        req.Content = JsonContent.Create(new
        {
            dropoffPoint = new { lat = 52.2600, lng = 21.0600 }
        });

        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ─── DriverCancelRide ─────────────────────────────────────────────────────

    [Fact]
    public async Task DriverCancelRide_ShouldReturn404_WhenRideNotFound()
    {
        var driverId = Guid.NewGuid().ToString();

        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/driver/rides/{Guid.NewGuid()}/cancel");
        req.Headers.Add("X-User-Id", driverId);
        req.Content = JsonContent.Create(new { reason = (string?)null });

        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DriverCancelRide_ShouldReturn401_WhenMissingUserId()
    {
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/driver/rides/{Guid.NewGuid()}/cancel");
        req.Content = JsonContent.Create(new { reason = (string?)null });

        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
