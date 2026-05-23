using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace TripPlanner.UnitTests.E2E.flows;

// Full happy-path E2E flow test: driver registers a route, passenger creates a match
// request, selects the driver, driver accepts, ride proceeds through arrival, start,
// and completion.
//
// Required externally:
//   - trip-planner service running on http://localhost:5238
//   - Postgres on localhost:5433 (trip-planner DB, service area seeded)
//   - RabbitMQ on localhost:5672 (compute queue)
//   - route-calc service running and consuming from RabbitMQ
//     (needed to activate the driver route and deliver match results)
//
// SSE note: the driver route SSE endpoint currently keys the channel by driverId
// (from X-User-Id), while RouteComputedHandler pushes by result.JobId. These must
// match for driver SSE to work — see DriverRouteEndpoints.cs GetRouteStatusEndpoint.
// Passenger request SSE is keyed by requestId and pushes are keyed correctly.
//
// Timeout: 30 s per SSE wait step (CancellationTokenSource).
public class FullRideFlowE2ETests
{
    private readonly HttpClient _client;

    public FullRideFlowE2ETests()
    {
        _client = new HttpClient { BaseAddress = new Uri("http://localhost:5238") };
    }

    [Fact]
    public async Task FullRideFlow_RegisterRouteAndCreateRequest_ShouldReachSearchingState()
    {
        // ─────────────────────────────────────────────────────────────────────
        // This partial flow test verifies the initial steps that do not require
        // route-calc: driver registers a route, passenger creates a request,
        // passenger can cancel the request.
        // ─────────────────────────────────────────────────────────────────────

        var driverId    = Guid.NewGuid().ToString();
        var passengerId = Guid.NewGuid().ToString();

        // 1. Driver registers route.
        var routeResp = await RegisterRouteAsync(driverId,
            start: (52.2297, 21.0122),
            end:   (52.2500, 21.0300));

        Assert.Equal(HttpStatusCode.Accepted, routeResp.Status);

        // 2. Passenger creates a route request.
        var requestResp = await CreatePassengerRequestAsync(passengerId,
            start: (52.2310, 21.0140),
            end:   (52.2480, 21.0280));

        Assert.Equal(HttpStatusCode.Accepted, requestResp.Status);
        Assert.NotEqual(Guid.Empty, requestResp.RequestId);

        // 3. Passenger cancels the request (no route-calc needed).
        var cancelReq = new HttpRequestMessage(HttpMethod.Delete,
            $"/api/passenger/route-requests/{requestResp.RequestId}");
        cancelReq.Headers.Add("X-User-Id", passengerId);

        var cancelResp = await _client.SendAsync(cancelReq);

        Assert.Equal(HttpStatusCode.NoContent, cancelResp.StatusCode);
    }

    [Fact]
    public async Task FullRideFlow_ShouldReceiveMatchesAndCompleteRide()
    {
        // ─────────────────────────────────────────────────────────────────────
        // FULL FLOW — requires route-calc running.
        //
        // Steps:
        //   1. Driver registers route, waits for "drier_route_computed" SSE event.
        //   2. Passenger creates route request, waits for "routes_ready" SSE event.
        //   3. Passenger selects driver route from match results.
        //   4. Driver confirms (accepts) via confirmation endpoint.
        //      SSE "match_confirmed" is pushed to passenger.
        //   5. Driver marks arrived.
        //   6. Driver starts ride.
        //   7. Driver completes ride.
        // ─────────────────────────────────────────────────────────────────────

        using var overallCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var driverId    = Guid.NewGuid().ToString();
        var passengerId = Guid.NewGuid().ToString();

        // ── Step 1: Driver registers route ────────────────────────────────────

        // Open driver SSE before registering so we don't miss the event.
        // Note: hub registers channel by driverId; RouteComputedHandler pushes by jobId.
        // For the event to arrive the routing key must align — see code comment at top.
        var driverSseRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/api/driver/route/{Guid.NewGuid()}/events");
        driverSseRequest.Headers.Add("X-User-Id", driverId);
        driverSseRequest.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var driverSseResp = await _client.SendAsync(driverSseRequest,
            HttpCompletionOption.ResponseHeadersRead, overallCts.Token);
        driverSseResp.EnsureSuccessStatusCode();

        var routeResp = await RegisterRouteAsync(driverId,
            start: (52.2297, 21.0122),
            end:   (52.2500, 21.0300));

        Assert.Equal(HttpStatusCode.Accepted, routeResp.Status);

        // Wait for route-calc to respond — "drier_route_computed" event on driver SSE.
        var driverSseEvent = await ReadNextSseEventAsync(
            driverSseResp, "drier_route_computed",
            TimeSpan.FromSeconds(30), overallCts.Token);

        Assert.True(driverSseEvent.Found,
            "Timed out waiting for drier_route_computed. " +
            "Ensure route-calc is running and connected to RabbitMQ.");

        // ── Step 2: Passenger creates route request ───────────────────────────

        // Open passenger SSE before creating request.
        Guid passengerRequestId;
        {
            var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/passenger/route-requests");
            createReq.Headers.Add("X-User-Id", passengerId);
            createReq.Content = JsonContent.Create(new
            {
                start = new { lat = 52.2310, lng = 21.0140 },
                end   = new { lat = 52.2480, lng = 21.0280 }
            });
            var createResp = await _client.SendAsync(createReq, overallCts.Token);
            createResp.EnsureSuccessStatusCode();

            var dto = await createResp.Content.ReadFromJsonAsync<PassengerRequestDto>(
                cancellationToken: overallCts.Token);
            passengerRequestId = dto!.RequestId;
        }

        // Open passenger SSE after we know the requestId.
        var passengerSseReq = new HttpRequestMessage(HttpMethod.Get,
            $"/api/passenger/route-requests/{passengerRequestId}/events");
        passengerSseReq.Headers.Add("X-User-Id", passengerId);
        passengerSseReq.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var passengerSseResp = await _client.SendAsync(passengerSseReq,
            HttpCompletionOption.ResponseHeadersRead, overallCts.Token);
        passengerSseResp.EnsureSuccessStatusCode();

        // Wait for route-calc to deliver match results — "routes_ready" event.
        var routesReadyEvent = await ReadNextSseEventAsync(
            passengerSseResp, "routes_ready",
            TimeSpan.FromSeconds(30), overallCts.Token);

        Assert.True(routesReadyEvent.Found,
            "Timed out waiting for routes_ready. " +
            "Ensure route-calc is running and connected to RabbitMQ.");

        // Parse the first match entry to get driverRouteId.
        var matchData    = JsonDocument.Parse(routesReadyEvent.Data!);
        var matches      = matchData.RootElement.GetProperty("Matches");
        var driverRouteId = matches[0].GetProperty("DriverRouteId").GetGuid();

        // ── Step 3: Passenger selects the driver ──────────────────────────────

        var selectReq = new HttpRequestMessage(HttpMethod.Post,
            $"/api/passenger/route-requests/{passengerRequestId}/select");
        selectReq.Headers.Add("X-User-Id", passengerId);
        selectReq.Content = JsonContent.Create(new { driverRouteId });

        var selectResp = await _client.SendAsync(selectReq, overallCts.Token);
        Assert.Equal(HttpStatusCode.NoContent, selectResp.StatusCode);

        // Passenger SSE should emit "awaiting_driver".
        var awaitingEvent = await ReadNextSseEventAsync(
            passengerSseResp, "awaiting_driver",
            TimeSpan.FromSeconds(5), overallCts.Token);
        Assert.True(awaitingEvent.Found, "Did not receive awaiting_driver SSE event.");

        // ── Step 4: Driver accepts the match ──────────────────────────────────

        var confirmReq = new HttpRequestMessage(HttpMethod.Post,
            $"/api/driver/requests/{passengerRequestId}/confirmation");
        confirmReq.Headers.Add("X-User-Id", driverId);
        confirmReq.Content = JsonContent.Create(new { accepted = true });

        var confirmResp = await _client.SendAsync(confirmReq, overallCts.Token);
        Assert.Equal(HttpStatusCode.NoContent, confirmResp.StatusCode);

        // Passenger SSE should emit "match_confirmed" containing the rideId.
        var matchConfirmedEvent = await ReadNextSseEventAsync(
            passengerSseResp, "match_confirmed",
            TimeSpan.FromSeconds(5), overallCts.Token);
        Assert.True(matchConfirmedEvent.Found, "Did not receive match_confirmed SSE event.");

        var matchConfirmedData = JsonDocument.Parse(matchConfirmedEvent.Data!);
        var rideId = matchConfirmedData.RootElement.GetProperty("RideId").GetGuid();

        // ── Step 5: Driver marks arrived ──────────────────────────────────────

        var arrivedReq = new HttpRequestMessage(HttpMethod.Post,
            $"/api/driver/rides/{rideId}/arrived");
        arrivedReq.Headers.Add("X-User-Id", driverId);

        var arrivedResp = await _client.SendAsync(arrivedReq, overallCts.Token);
        Assert.Equal(HttpStatusCode.NoContent, arrivedResp.StatusCode);

        // ── Step 6: Driver starts ride ────────────────────────────────────────

        var startDriverReq = new HttpRequestMessage(HttpMethod.Post,
            $"/api/driver/rides/{rideId}/start");
        startDriverReq.Headers.Add("X-User-Id", driverId);

        var startDriverResp = await _client.SendAsync(startDriverReq, overallCts.Token);
        Assert.Equal(HttpStatusCode.NoContent, startDriverResp.StatusCode);

        // ── Step 7: Driver completes ride ─────────────────────────────────────

        var completeReq = new HttpRequestMessage(HttpMethod.Post,
            $"/api/driver/rides/{rideId}/complete");
        completeReq.Headers.Add("X-User-Id", driverId);
        completeReq.Content = JsonContent.Create(new
        {
            dropoffPoint = new { lat = 52.2480, lng = 21.0280 }
        });

        var completeResp = await _client.SendAsync(completeReq, overallCts.Token);
        Assert.Equal(HttpStatusCode.NoContent, completeResp.StatusCode);
    }

    [Fact]
    public async Task FullRideFlow_DriverDeclines_ShouldUpdateMatchResults()
    {
        // Requires route-calc running.
        // After driver declines, the match entry is removed from the request
        // and (if matches remain) "routes_ready" is re-emitted on the passenger SSE.

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var driverId    = Guid.NewGuid().ToString();
        var passengerId = Guid.NewGuid().ToString();

        // Register + wait for activation.
        var driverSseReq = new HttpRequestMessage(HttpMethod.Get,
            $"/api/driver/route/{Guid.NewGuid()}/events");
        driverSseReq.Headers.Add("X-User-Id", driverId);
        driverSseReq.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var driverSseResp = await _client.SendAsync(driverSseReq,
            HttpCompletionOption.ResponseHeadersRead, cts.Token);

        await RegisterRouteAsync(driverId, (52.2297, 21.0122), (52.2500, 21.0300));

        var computed = await ReadNextSseEventAsync(driverSseResp, "drier_route_computed",
            TimeSpan.FromSeconds(30), cts.Token);
        Assert.True(computed.Found, "Timed out waiting for drier_route_computed.");

        // Create passenger request.
        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/passenger/route-requests");
        createReq.Headers.Add("X-User-Id", passengerId);
        createReq.Content = JsonContent.Create(new
        {
            start = new { lat = 52.2310, lng = 21.0140 },
            end   = new { lat = 52.2480, lng = 21.0280 }
        });
        var createResp = await _client.SendAsync(createReq, cts.Token);
        var dto = await createResp.Content.ReadFromJsonAsync<PassengerRequestDto>(
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

        var routesReady = await ReadNextSseEventAsync(passengerSseResp, "routes_ready",
            TimeSpan.FromSeconds(30), cts.Token);
        Assert.True(routesReady.Found, "Timed out waiting for routes_ready.");

        var matchData     = JsonDocument.Parse(routesReady.Data!);
        var driverRouteId = matchData.RootElement.GetProperty("Matches")[0]
            .GetProperty("DriverRouteId").GetGuid();

        // Passenger selects driver.
        var selectReq = new HttpRequestMessage(HttpMethod.Post,
            $"/api/passenger/route-requests/{requestId}/select");
        selectReq.Headers.Add("X-User-Id", passengerId);
        selectReq.Content = JsonContent.Create(new { driverRouteId });
        await _client.SendAsync(selectReq, cts.Token);

        // Driver declines.
        var declineReq = new HttpRequestMessage(HttpMethod.Post,
            $"/api/driver/requests/{requestId}/confirmation");
        declineReq.Headers.Add("X-User-Id", driverId);
        declineReq.Content = JsonContent.Create(new { accepted = false });

        var declineResp = await _client.SendAsync(declineReq, cts.Token);
        Assert.Equal(HttpStatusCode.NoContent, declineResp.StatusCode);

        // Passenger SSE should emit "match_declined".
        var declinedEvent = await ReadNextSseEventAsync(passengerSseResp, "match_declined",
            TimeSpan.FromSeconds(5), cts.Token);
        Assert.True(declinedEvent.Found, "Did not receive match_declined SSE event.");
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private async Task<(HttpStatusCode Status, Guid JobId)> RegisterRouteAsync(
        string driverId,
        (double Lat, double Lng) start,
        (double Lat, double Lng) end)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/driver/route");
        req.Headers.Add("X-User-Id", driverId);
        req.Content = JsonContent.Create(new
        {
            start = new { lat = start.Lat, lng = start.Lng },
            end   = new { lat = end.Lat,   lng = end.Lng   }
        });
        var resp = await _client.SendAsync(req);
        var dto  = await resp.Content.ReadFromJsonAsync<RegisterRouteDto>();
        return (resp.StatusCode, dto?.JobId ?? Guid.Empty);
    }

    private async Task<(HttpStatusCode Status, Guid RequestId)> CreatePassengerRequestAsync(
        string passengerId,
        (double Lat, double Lng) start,
        (double Lat, double Lng) end)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/passenger/route-requests");
        req.Headers.Add("X-User-Id", passengerId);
        req.Content = JsonContent.Create(new
        {
            start = new { lat = start.Lat, lng = start.Lng },
            end   = new { lat = end.Lat,   lng = end.Lng   }
        });
        var resp = await _client.SendAsync(req);
        var dto  = await resp.Content.ReadFromJsonAsync<PassengerRequestDto>();
        return (resp.StatusCode, dto?.RequestId ?? Guid.Empty);
    }

    // Reads SSE lines until the given eventType is found or the timeout expires.
    private static async Task<(bool Found, string? Data)> ReadNextSseEventAsync(
        HttpResponseMessage sseResponse,
        string targetEventType,
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
                else if (line.Length == 0 && eventType == targetEventType)
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

    private record RegisterRouteDto(Guid JobId);
    private record PassengerRequestDto(Guid RequestId);
}
