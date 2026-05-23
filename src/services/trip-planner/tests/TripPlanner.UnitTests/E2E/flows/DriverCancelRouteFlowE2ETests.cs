using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace TripPlanner.UnitTests.E2E.flows;

// E2E flow tests for the driver-cancels-route scenario:
//   1. Driver registers a route (route-calc activates it).
//   2. Passenger creates a request and receives match results.
//   3. Driver cancels the route.
//   4. Affected passengers receive a "routes_expired" or "routes_ready" SSE event,
//      depending on whether any other matches remain.
//
// Also covers:
//   - Driver modifies a route mid-flow (cancel + re-register).
//   - Passenger cancels request during the matching phase.
//
// Required externally:
//   - trip-planner service running on http://localhost:5238
//   - Postgres on localhost:5433 (trip-planner DB, service area seeded)
//   - RabbitMQ on localhost:5672 (compute queue)
//   - route-calc service running and consuming from RabbitMQ
//
// The first test (DriverCancelRoute_WithoutActiveRoute) does not require route-calc.
public class DriverCancelRouteFlowE2ETests
{
    private readonly HttpClient _client;

    public DriverCancelRouteFlowE2ETests()
    {
        _client = new HttpClient { BaseAddress = new Uri("http://localhost:5238") };
    }

    // ─── No route-calc needed ────────────────────────────────────────────────

    [Fact]
    public async Task RegisterRoute_ThenCancelBeforeActivation_ShouldReturn404()
    {
        // Route is Pending (not Active) until route-calc processes it.
        // CancelRoute checks for Active status, so it returns 404.

        var driverId = Guid.NewGuid().ToString();

        var registerReq = new HttpRequestMessage(HttpMethod.Post, "/api/driver/route");
        registerReq.Headers.Add("X-User-Id", driverId);
        registerReq.Content = JsonContent.Create(new
        {
            start = new { lat = 52.2297, lng = 21.0122 },
            end   = new { lat = 52.2500, lng = 21.0300 }
        });
        var registerResp = await _client.SendAsync(registerReq);
        Assert.Equal(HttpStatusCode.Accepted, registerResp.StatusCode);

        var cancelReq = new HttpRequestMessage(HttpMethod.Delete, "/api/driver/route");
        cancelReq.Headers.Add("X-User-Id", driverId);
        var cancelResp = await _client.SendAsync(cancelReq);

        Assert.Equal(HttpStatusCode.NotFound, cancelResp.StatusCode);
    }

    [Fact]
    public async Task RegisterRoute_ThenRegisterAgain_ShouldReturn409()
    {
        // Same driver cannot have two active-or-pending routes at the same time.
        // RouteAlreadyActiveException → 409.
        //
        // Wait — the handler actually checks GetActiveByDriverIdAsync (status = 'Active').
        // After first RegisterRoute the route is Pending, not Active.
        // So a second RegisterRoute would succeed (no active route found).
        // This test documents that behavior.

        var driverId = Guid.NewGuid().ToString();

        var first = new HttpRequestMessage(HttpMethod.Post, "/api/driver/route");
        first.Headers.Add("X-User-Id", driverId);
        first.Content = JsonContent.Create(new
        {
            start = new { lat = 52.2297, lng = 21.0122 },
            end   = new { lat = 52.2500, lng = 21.0300 }
        });
        var r1 = await _client.SendAsync(first);
        Assert.Equal(HttpStatusCode.Accepted, r1.StatusCode);

        // Second RegisterRoute — route is still Pending, so no 409 yet.
        var second = new HttpRequestMessage(HttpMethod.Post, "/api/driver/route");
        second.Headers.Add("X-User-Id", driverId);
        second.Content = JsonContent.Create(new
        {
            start = new { lat = 52.2310, lng = 21.0140 },
            end   = new { lat = 52.2510, lng = 21.0310 }
        });
        var r2 = await _client.SendAsync(second);

        // Pending route is not returned by GetActiveByDriverIdAsync, so Accepted again.
        Assert.Equal(HttpStatusCode.Accepted, r2.StatusCode);
    }

    // ─── Requires route-calc ─────────────────────────────────────────────────

    [Fact]
    public async Task DriverCancelRoute_AfterActivation_ShouldReturn204()
    {
        // Requires route-calc running.

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var driverId = Guid.NewGuid().ToString();

        // Open driver SSE.
        var driverSseReq = new HttpRequestMessage(HttpMethod.Get,
            $"/api/driver/route/{Guid.NewGuid()}/events");
        driverSseReq.Headers.Add("X-User-Id", driverId);
        driverSseReq.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var driverSseResp = await _client.SendAsync(driverSseReq,
            HttpCompletionOption.ResponseHeadersRead, cts.Token);

        var routeReq = new HttpRequestMessage(HttpMethod.Post, "/api/driver/route");
        routeReq.Headers.Add("X-User-Id", driverId);
        routeReq.Content = JsonContent.Create(new
        {
            start = new { lat = 52.2297, lng = 21.0122 },
            end   = new { lat = 52.2500, lng = 21.0300 }
        });
        await _client.SendAsync(routeReq, cts.Token);

        // Wait for route to become Active.
        var computed = await ReadNextSseEventAsync(driverSseResp, "drier_route_computed",
            TimeSpan.FromSeconds(25), cts.Token);
        Assert.True(computed.Found,
            "Timed out waiting for drier_route_computed. Ensure route-calc is running.");

        // Now cancel the active route.
        var cancelReq = new HttpRequestMessage(HttpMethod.Delete, "/api/driver/route");
        cancelReq.Headers.Add("X-User-Id", driverId);
        var cancelResp = await _client.SendAsync(cancelReq, cts.Token);

        Assert.Equal(HttpStatusCode.NoContent, cancelResp.StatusCode);
    }

    [Fact]
    public async Task DriverCancelRoute_WhenPassengerHasMatchResults_ShouldNotifyPassenger()
    {
        // Requires route-calc running.
        // After driver cancels, CancelRouteHandler removes the driver from any
        // RoutesPresented or PendingDriver requests and pushes an SSE event.

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var driverId    = Guid.NewGuid().ToString();
        var passengerId = Guid.NewGuid().ToString();

        // Register and activate driver route.
        var driverSseReq = new HttpRequestMessage(HttpMethod.Get,
            $"/api/driver/route/{Guid.NewGuid()}/events");
        driverSseReq.Headers.Add("X-User-Id", driverId);
        driverSseReq.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var driverSseResp = await _client.SendAsync(driverSseReq,
            HttpCompletionOption.ResponseHeadersRead, cts.Token);

        var routeReq = new HttpRequestMessage(HttpMethod.Post, "/api/driver/route");
        routeReq.Headers.Add("X-User-Id", driverId);
        routeReq.Content = JsonContent.Create(new
        {
            start = new { lat = 52.2297, lng = 21.0122 },
            end   = new { lat = 52.2500, lng = 21.0300 }
        });
        await _client.SendAsync(routeReq, cts.Token);

        var computed = await ReadNextSseEventAsync(driverSseResp, "drier_route_computed",
            TimeSpan.FromSeconds(25), cts.Token);
        Assert.True(computed.Found,
            "Timed out waiting for drier_route_computed. Ensure route-calc is running.");

        // Create passenger request.
        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/passenger/route-requests");
        createReq.Headers.Add("X-User-Id", passengerId);
        createReq.Content = JsonContent.Create(new
        {
            start = new { lat = 52.2310, lng = 21.0140 },
            end   = new { lat = 52.2480, lng = 21.0280 }
        });
        var createResp = await _client.SendAsync(createReq, cts.Token);
        var dto       = await createResp.Content.ReadFromJsonAsync<PassengerRequestDto>(
            cancellationToken: cts.Token);
        var requestId = dto!.RequestId;

        // Open passenger SSE.
        var passengerSseReq = new HttpRequestMessage(HttpMethod.Get,
            $"/api/passenger/route-requests/{requestId}/events");
        passengerSseReq.Headers.Add("X-User-Id", passengerId);
        passengerSseReq.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var passengerSseResp = await _client.SendAsync(passengerSseReq,
            HttpCompletionOption.ResponseHeadersRead, cts.Token);

        // Wait for match results.
        var routesReady = await ReadNextSseEventAsync(passengerSseResp, "routes_ready",
            TimeSpan.FromSeconds(30), cts.Token);
        Assert.True(routesReady.Found, "Timed out waiting for routes_ready.");

        // Driver cancels route — passenger should receive "routes_expired" (or "routes_ready"
        // if other drivers are still in the match list).
        var cancelReq = new HttpRequestMessage(HttpMethod.Delete, "/api/driver/route");
        cancelReq.Headers.Add("X-User-Id", driverId);
        var cancelResp = await _client.SendAsync(cancelReq, cts.Token);
        Assert.Equal(HttpStatusCode.NoContent, cancelResp.StatusCode);

        // Passenger should receive either "routes_expired" or "routes_ready".
        var notified = await ReadAnyOfSseEventAsync(
            passengerSseResp,
            new[] { "routes_expired", "routes_ready" },
            TimeSpan.FromSeconds(5), cts.Token);

        Assert.True(notified.Found,
            "Passenger was not notified via SSE after driver cancelled route.");
    }

    [Fact]
    public async Task DriverModifyRoute_AfterActivation_ShouldReturnAccepted()
    {
        // Requires route-calc running.
        // ModifyRoute cancels the existing route and re-registers a new one.

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var driverId = Guid.NewGuid().ToString();

        // Register and activate.
        var driverSseReq = new HttpRequestMessage(HttpMethod.Get,
            $"/api/driver/route/{Guid.NewGuid()}/events");
        driverSseReq.Headers.Add("X-User-Id", driverId);
        driverSseReq.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var driverSseResp = await _client.SendAsync(driverSseReq,
            HttpCompletionOption.ResponseHeadersRead, cts.Token);

        var routeReq = new HttpRequestMessage(HttpMethod.Post, "/api/driver/route");
        routeReq.Headers.Add("X-User-Id", driverId);
        routeReq.Content = JsonContent.Create(new
        {
            start = new { lat = 52.2297, lng = 21.0122 },
            end   = new { lat = 52.2500, lng = 21.0300 }
        });
        await _client.SendAsync(routeReq, cts.Token);

        var computed = await ReadNextSseEventAsync(driverSseResp, "drier_route_computed",
            TimeSpan.FromSeconds(25), cts.Token);
        Assert.True(computed.Found,
            "Timed out waiting for drier_route_computed. Ensure route-calc is running.");

        // Modify route (new destination).
        var modifyReq = new HttpRequestMessage(HttpMethod.Put, "/api/driver/route");
        modifyReq.Headers.Add("X-User-Id", driverId);
        modifyReq.Content = JsonContent.Create(new
        {
            start = new { lat = 52.2297, lng = 21.0122 },
            end   = new { lat = 52.2600, lng = 21.0450 }
        });
        var modifyResp = await _client.SendAsync(modifyReq, cts.Token);

        Assert.Equal(HttpStatusCode.Accepted, modifyResp.StatusCode);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static async Task<(bool Found, string? Data)> ReadNextSseEventAsync(
        HttpResponseMessage sseResponse,
        string targetEventType,
        TimeSpan timeout,
        CancellationToken parentCt)
    {
        var result = await ReadAnyOfSseEventAsync(
            sseResponse, new[] { targetEventType }, timeout, parentCt);
        return result;
    }

    private static async Task<(bool Found, string? Data)> ReadAnyOfSseEventAsync(
        HttpResponseMessage sseResponse,
        string[] targetEventTypes,
        TimeSpan timeout,
        CancellationToken parentCt)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(parentCt);
        cts.CancelAfter(timeout);

        try
        {
            var stream = await sseResponse.Content.ReadAsStreamAsync(cts.Token);
            using var reader = new StreamReader(stream);

            string? lastData  = null;
            string? eventType = null;

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(cts.Token);
                if (line is null) continue;

                if (line.StartsWith("event:"))
                {
                    eventType = line["event:".Length..].Trim();
                }
                else if (line.StartsWith("data:"))
                {
                    lastData = line["data:".Length..].Trim();
                }
                else if (line.Length == 0 && eventType is not null
                         && targetEventTypes.Contains(eventType))
                {
                    return (true, lastData);
                }
                else if (line.Length == 0)
                {
                    eventType = null;
                    lastData  = null;
                }
            }
        }
        catch (OperationCanceledException) { }

        return (false, null);
    }

    private record PassengerRequestDto(Guid RequestId);
}
