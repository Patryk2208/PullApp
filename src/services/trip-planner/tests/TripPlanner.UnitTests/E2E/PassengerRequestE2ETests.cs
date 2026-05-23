using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace TripPlanner.UnitTests.E2E;

// E2E tests for:
//   POST   /api/passenger/route-requests           (CreateRouteRequestHandler)
//   GET    /api/passenger/route-requests/{id}/events (SSE stream)
//   DELETE /api/passenger/route-requests/{id}       (CancelRouteRequestHandler)
//   POST   /api/passenger/route-requests/{id}/select (SelectRouteHandler)
//
// Required externally:
//   - trip-planner service running on http://localhost:5238
//   - Postgres on localhost:5433 (trip-planner DB, service area seeded)
//   - RabbitMQ on localhost:5672  (needed for queue.PublishAsync)
//
// SelectRoute happy-path additionally requires route-calc running
// and a request in RoutesPresented status — see flows/FullRideFlowE2ETests.
public class PassengerRequestE2ETests
{
    private readonly HttpClient _client;

    public PassengerRequestE2ETests()
    {
        _client = new HttpClient { BaseAddress = new Uri("http://localhost:5238") };
    }

    // ─── CreateRouteRequest ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateRouteRequest_ShouldReturnAccepted()
    {
        var passengerId = Guid.NewGuid().ToString();

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/passenger/route-requests");
        req.Headers.Add("X-User-Id", passengerId);
        req.Content = JsonContent.Create(new
        {
            start = new { lat = 52.2297, lng = 21.0122 },
            end   = new { lat = 52.2400, lng = 21.0500 }
        });

        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task CreateRouteRequest_ShouldReturn409_WhenActiveRequestAlreadyExists()
    {
        // Same passenger submits a second request while the first is still active.

        var passengerId = Guid.NewGuid().ToString();

        var first = new HttpRequestMessage(HttpMethod.Post, "/api/passenger/route-requests");
        first.Headers.Add("X-User-Id", passengerId);
        first.Content = JsonContent.Create(new
        {
            start = new { lat = 52.2297, lng = 21.0122 },
            end   = new { lat = 52.2400, lng = 21.0500 }
        });
        var r1 = await _client.SendAsync(first);
        r1.EnsureSuccessStatusCode();

        var second = new HttpRequestMessage(HttpMethod.Post, "/api/passenger/route-requests");
        second.Headers.Add("X-User-Id", passengerId);
        second.Content = JsonContent.Create(new
        {
            start = new { lat = 52.2297, lng = 21.0122 },
            end   = new { lat = 52.2400, lng = 21.0500 }
        });

        var r2 = await _client.SendAsync(second);

        Assert.Equal(HttpStatusCode.Conflict, r2.StatusCode);
    }

    [Fact]
    public async Task CreateRouteRequest_ShouldReturn422_WhenPointOutsideServiceArea()
    {
        var passengerId = Guid.NewGuid().ToString();

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/passenger/route-requests");
        req.Headers.Add("X-User-Id", passengerId);
        req.Content = JsonContent.Create(new
        {
            start = new { lat = 51.5074, lng = -0.1278 }, // London — outside Warsaw service area
            end   = new { lat = 52.2400, lng = 21.0500 }
        });

        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task CreateRouteRequest_ShouldReturn401_WhenMissingUserId()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/passenger/route-requests");
        req.Content = JsonContent.Create(new
        {
            start = new { lat = 52.2297, lng = 21.0122 },
            end   = new { lat = 52.2400, lng = 21.0500 }
        });

        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ─── SSE stream ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SseStream_ShouldReturn200WithEventStreamContentType()
    {
        var passengerId = Guid.NewGuid().ToString();
        var requestId   = Guid.NewGuid();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var req = new HttpRequestMessage(HttpMethod.Get,
            $"/api/passenger/route-requests/{requestId}/events");
        req.Headers.Add("X-User-Id", passengerId);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var response = await _client.SendAsync(req,
            HttpCompletionOption.ResponseHeadersRead, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream",
            response.Content.Headers.ContentType?.MediaType);

        cts.Cancel();
    }

    // ─── CancelRouteRequest ───────────────────────────────────────────────────

    [Fact]
    public async Task CancelRouteRequest_ShouldReturn204()
    {
        var passengerId = Guid.NewGuid().ToString();

        // Create request first.
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

        // Cancel it.
        var cancelReq = new HttpRequestMessage(HttpMethod.Delete,
            $"/api/passenger/route-requests/{requestId}");
        cancelReq.Headers.Add("X-User-Id", passengerId);

        var cancelResp = await _client.SendAsync(cancelReq);

        Assert.Equal(HttpStatusCode.NoContent, cancelResp.StatusCode);
    }

    [Fact]
    public async Task CancelRouteRequest_ShouldReturn404_WhenRequestNotFound()
    {
        var passengerId = Guid.NewGuid().ToString();
        var unknownId   = Guid.NewGuid();

        var req = new HttpRequestMessage(HttpMethod.Delete,
            $"/api/passenger/route-requests/{unknownId}");
        req.Headers.Add("X-User-Id", passengerId);

        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CancelRouteRequest_ShouldReturn403_WhenCallerIsNotOwner()
    {
        var ownerPassengerId = Guid.NewGuid().ToString();
        var otherPassengerId = Guid.NewGuid().ToString();

        // Create as owner.
        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/passenger/route-requests");
        createReq.Headers.Add("X-User-Id", ownerPassengerId);
        createReq.Content = JsonContent.Create(new
        {
            start = new { lat = 52.2297, lng = 21.0122 },
            end   = new { lat = 52.2400, lng = 21.0500 }
        });
        var createResp = await _client.SendAsync(createReq);
        createResp.EnsureSuccessStatusCode();

        var body      = await createResp.Content.ReadFromJsonAsync<PassengerRouteRequestResponseDto>();
        var requestId = body!.RequestId;

        // Cancel as different user.
        var cancelReq = new HttpRequestMessage(HttpMethod.Delete,
            $"/api/passenger/route-requests/{requestId}");
        cancelReq.Headers.Add("X-User-Id", otherPassengerId);

        var cancelResp = await _client.SendAsync(cancelReq);

        Assert.Equal(HttpStatusCode.Forbidden, cancelResp.StatusCode);
    }

    // ─── SelectRoute ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SelectRoute_ShouldReturn404_WhenRequestNotFound()
    {
        var passengerId = Guid.NewGuid().ToString();
        var unknownId   = Guid.NewGuid();

        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/passenger/route-requests/{unknownId}/select");
        req.Headers.Add("X-User-Id", passengerId);
        req.Content = JsonContent.Create(new { driverRouteId = Guid.NewGuid() });

        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SelectRoute_ShouldReturn409_WhenRequestIsNotInRoutesPresented()
    {
        // A freshly created request is in Searching state, not RoutesPresented.
        var passengerId = Guid.NewGuid().ToString();

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

        var selectReq = new HttpRequestMessage(HttpMethod.Post,
            $"/api/passenger/route-requests/{requestId}/select");
        selectReq.Headers.Add("X-User-Id", passengerId);
        selectReq.Content = JsonContent.Create(new { driverRouteId = Guid.NewGuid() });

        var selectResp = await _client.SendAsync(selectReq);

        Assert.Equal(HttpStatusCode.Conflict, selectResp.StatusCode);
    }

    // ─── response DTO ─────────────────────────────────────────────────────────

    private record PassengerRouteRequestResponseDto(Guid RequestId);
}
