using System.Net;
using System.Net.Http.Json;

namespace TripPlanner.UnitTests.E2E;

// E2E tests for POST /api/driver/requests/{requestId}/confirmation
// (ConfirmationHandler — driver accepts or declines a match)
//
// Required externally:
//   - trip-planner service running on http://localhost:5238
//   - Postgres on localhost:5433 (trip-planner DB, service area seeded)
//   - RabbitMQ on localhost:5672
//
// Happy-path tests (Accept/Decline with a real PendingDriver request) additionally
// require route-calc running so that a match result arrives and the passenger can
// select a route. The full flow is tested in flows/FullRideFlowE2ETests.
public class DriverConfirmationE2ETests
{
    private readonly HttpClient _client;

    public DriverConfirmationE2ETests()
    {
        _client = new HttpClient { BaseAddress = new Uri("http://localhost:5238") };
    }

    [Fact]
    public async Task Confirmation_ShouldReturn404_WhenRequestNotFound()
    {
        var driverId  = Guid.NewGuid().ToString();
        var unknownId = Guid.NewGuid();

        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/driver/requests/{unknownId}/confirmation");
        req.Headers.Add("X-User-Id", driverId);
        req.Content = JsonContent.Create(new { accepted = true });

        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Confirmation_ShouldReturn409_WhenRequestIsNotPendingDriver()
    {
        // A request in Searching state (just created by the passenger) is not PendingDriver.
        // The confirmation handler requires PendingDriver status.

        var passengerId = Guid.NewGuid().ToString();
        var driverId    = Guid.NewGuid().ToString();

        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/passenger/route-requests");
        createReq.Headers.Add("X-User-Id", passengerId);
        createReq.Content = JsonContent.Create(new
        {
            start = new { lat = 52.2297, lng = 21.0122 },
            end   = new { lat = 52.2400, lng = 21.0500 }
        });
        var createResp = await _client.SendAsync(createReq);
        createResp.EnsureSuccessStatusCode();

        var body      = await createResp.Content.ReadFromJsonAsync<PassengerRouteRequestResponseDto>();
        var requestId = body!.RequestId;

        var confirmReq = new HttpRequestMessage(HttpMethod.Post,
            $"/api/driver/requests/{requestId}/confirmation");
        confirmReq.Headers.Add("X-User-Id", driverId);
        confirmReq.Content = JsonContent.Create(new { accepted = true });

        var confirmResp = await _client.SendAsync(confirmReq);

        // InvalidStateTransitionException maps to 409.
        Assert.Equal(HttpStatusCode.Conflict, confirmResp.StatusCode);
    }

    [Fact]
    public async Task Confirmation_ShouldReturn401_WhenMissingUserId()
    {
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/driver/requests/{Guid.NewGuid()}/confirmation");
        req.Content = JsonContent.Create(new { accepted = true });

        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private record PassengerRouteRequestResponseDto(Guid RequestId);
}
