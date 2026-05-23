using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace TripPlanner.UnitTests.E2E.flows
{
    // Full happy-path cluster-level E2E flow tests executed through the Kubernetes Gateway.
    //
    // These tests observe the externally visible side-effects of the distributed pipeline:
    //   test runner -> gateway.pullapp.svc.cluster.local -> API gateway -> trip-planner -> ...
    //
    // Required externally:
    //   - Kubernetes cluster running
    //   - gateway service exposed as: http://gateway.pullapp.svc.cluster.local
    //   - trip-planner deployed in cluster
    //   - route-calc deployed and consuming compute jobs
    //   - Postgres running with seeded service area
    //
    // These tests intentionally do NOT call localhost.
    // They validate the cluster networking + gateway routing path.
    public class ClusterGatewayFullFlowE2ETests
    {
        private readonly HttpClient _client;

        public ClusterGatewayFullFlowE2ETests()
        {
            _client = new HttpClient
            {
                BaseAddress = new Uri("http://gateway.pullapp.svc.cluster.local")
            };
        }

        [Fact]
        public async Task FullClusterFlow_RegisterRouteAndPassengerRequest_ShouldAllowCancellation()
        {
            // ─────────────────────────────────────────────────────────────────────
            // This partial flow test verifies the initial steps that do not require
            // route-calc: driver registers a route through the gateway, passenger 
            // creates a request, passenger can cancel the request.
            // ─────────────────────────────────────────────────────────────────────

            var driverId = Guid.NewGuid().ToString();
            var passengerId = Guid.NewGuid().ToString();

            // 1. Driver registers route through gateway.
            var routeResp = await RegisterRouteAsync(driverId,
                start: (52.2297, 21.0122),
                end: (52.2500, 21.0300));

            Assert.Equal(HttpStatusCode.Accepted, routeResp.Status);
            Assert.NotEqual(Guid.Empty, routeResp.JobId);

            // 2. Passenger creates a route request.
            var requestResp = await CreatePassengerRequestAsync(passengerId,
                start: (52.2310, 21.0140),
                end: (52.2480, 21.0280));

            Assert.Equal(HttpStatusCode.Accepted, requestResp.Status);
            Assert.NotEqual(Guid.Empty, requestResp.RequestId);

            // 3. Passenger cancels the request.
            var cancelReq = new HttpRequestMessage(HttpMethod.Delete,
                $"/api/passenger/route-requests/{requestResp.RequestId}");
            cancelReq.Headers.Add("X-User-Id", passengerId);

            var cancelResp = await _client.SendAsync(cancelReq);

            Assert.Equal(HttpStatusCode.NoContent, cancelResp.StatusCode);
        }

        [Fact]
        public async Task FullClusterFlow_ShouldReceiveMatchesAndCompleteRide()
        {
            // ─────────────────────────────────────────────────────────────────────
            // FULL FLOW — requires route-calc running in the cluster.
            //
            // Steps:
            //   1. Driver registers route through gateway, waits for "drier_route_computed" SSE event.
            //   2. Passenger creates route request, waits for "routes_ready" SSE event.
            //   3. Passenger selects driver route from match results.
            //   4. Driver confirms (accepts) via confirmation endpoint.
            //      SSE "match_confirmed" is pushed to passenger.
            //   5. Driver marks arrived.
            //   6. Driver starts ride.
            //   7. Driver completes ride.
            // ─────────────────────────────────────────────────────────────────────

            using var overallCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

            var driverId = Guid.NewGuid().ToString();
            var passengerId = Guid.NewGuid().ToString();

            // ── Step 1: Driver registers route ────────────────────────────────────

            // Open driver SSE before registering so we don't miss the event.
            var driverRouteToken = Guid.NewGuid();
            var driverSseRequest = new HttpRequestMessage(HttpMethod.Get,
                $"/api/driver/route/{driverRouteToken}/events");
            driverSseRequest.Headers.Add("X-User-Id", driverId);
            driverSseRequest.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("text/event-stream"));

            var driverSseResp = await _client.SendAsync(driverSseRequest,
                HttpCompletionOption.ResponseHeadersRead, overallCts.Token);
            driverSseResp.EnsureSuccessStatusCode();

            // Register route now that the channel listener is safely established.
            var routeResp = await RegisterRouteAsync(driverId,
                start: (52.2297, 21.0122),
                end: (52.2500, 21.0300));

            Assert.Equal(HttpStatusCode.Accepted, routeResp.Status);

            // Wait for route-calc to respond — "drier_route_computed" event on driver SSE.
            var driverComputedEvent = await ReadNextSseEventAsync(
                driverSseResp, "drier_route_computed",
                TimeSpan.FromSeconds(45), overallCts.Token);

            Assert.True(driverComputedEvent.Found,
                "Timed out waiting for drier_route_computed. Check async pipeline processing.");
            Assert.False(string.IsNullOrWhiteSpace(driverComputedEvent.Data), "Driver SSE payload was empty.");

            // ── Step 2: Passenger creates route request ───────────────────────────

            // Open passenger SSE before creating request.
            Guid passengerRequestId;
            var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/passenger/route-requests");
            createReq.Headers.Add("X-User-Id", passengerId);
            createReq.Content = JsonContent.Create(new
            {
                start = new { lat = 52.2310, lng = 21.0140 },
                end = new { lat = 52.2480, lng = 21.0280 }
            });
            var createResp = await _client.SendAsync(createReq, overallCts.Token);
            createResp.EnsureSuccessStatusCode();

            var dto = await createResp.Content.ReadFromJsonAsync<PassengerRequestDto>(
                cancellationToken: overallCts.Token);
            Assert.NotNull(dto);
            passengerRequestId = dto!.RequestId;

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
                TimeSpan.FromSeconds(45), overallCts.Token);

            Assert.True(routesReadyEvent.Found, "Timed out waiting for routes_ready.");
            Assert.NotNull(routesReadyEvent.Data);

            // Parse match entry to get driverRouteId.
            var matchData = JsonDocument.Parse(routesReadyEvent.Data!);
            Assert.True(matchData.RootElement.TryGetProperty("Matches", out var matches),
                "routes_ready payload missing 'Matches' property.");
            Assert.True(matches.ValueKind == JsonValueKind.Array,
                "'Matches' property is not a valid JSON array.");
            Assert.True(matches.GetArrayLength() > 0,
                "Matches list returned empty.");

            var firstMatch = matches[0];
            Assert.True(firstMatch.TryGetProperty("DriverRouteId", out var matchedRouteIdProperty),
                "Match structure missing 'DriverRouteId'.");

            var driverRouteId = matchedRouteIdProperty.GetGuid();
            Assert.NotEqual(Guid.Empty, driverRouteId);

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
            Assert.NotNull(matchConfirmedEvent.Data);

            var matchConfirmedData = JsonDocument.Parse(matchConfirmedEvent.Data!);
            Assert.True(matchConfirmedData.RootElement.TryGetProperty("RideId", out var rideIdProperty),
                "match_confirmed event missing 'RideId'.");
            var rideId = rideIdProperty.GetGuid();
            Assert.NotEqual(Guid.Empty, rideId);

            // ── Step 5: Driver marks arrived ──────────────────────────────────────

            var arrivedReq = new HttpRequestMessage(HttpMethod.Post,
                $"/api/driver/rides/{rideId}/arrived");
            arrivedReq.Headers.Add("X-User-Id", driverId);

            var arrivedResp = await _client.SendAsync(arrivedReq, overallCts.Token);
            Assert.Equal(HttpStatusCode.NoContent, arrivedResp.StatusCode);

            // ── Step 6: Start Ride ────────────────────────────────────────────────

            var startDriverReq = new HttpRequestMessage(HttpMethod.Post,
                $"/api/driver/rides/{rideId}/start");
            startDriverReq.Headers.Add("X-User-Id", driverId);

            var startDriverResp = await _client.SendAsync(startDriverReq, overallCts.Token);
            Assert.Equal(HttpStatusCode.NoContent, startDriverResp.StatusCode);

            // ── Step 7: Complete Ride ─────────────────────────────────────────────

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
        public async Task FullClusterFlow_DriverDeclines_ShouldUpdateMatchResults()
        {
            // Requires route-calc running in the cluster.
            // After driver declines, the match entry is removed from the request
            // and (if matches remain) "routes_ready" is re-emitted on the passenger SSE.

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

            var driverId = Guid.NewGuid().ToString();
            var passengerId = Guid.NewGuid().ToString();

            // ── Step 1: Pre-establish Driver Listener & Register ──────────────────
            var driverRouteToken = Guid.NewGuid();
            var driverSseReq = new HttpRequestMessage(HttpMethod.Get,
                $"/api/driver/route/{driverRouteToken}/events");
            driverSseReq.Headers.Add("X-User-Id", driverId);
            driverSseReq.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("text/event-stream"));
            var driverSseResp = await _client.SendAsync(driverSseReq,
                HttpCompletionOption.ResponseHeadersRead, cts.Token);

            await RegisterRouteAsync(driverId, (52.2297, 21.0122), (52.2500, 21.0300));

            var computed = await ReadNextSseEventAsync(driverSseResp, "drier_route_computed",
                TimeSpan.FromSeconds(45), cts.Token);
            Assert.True(computed.Found, "Timed out waiting for drier_route_computed.");

            // ── Step 2: Passenger Request Placement ───────────────────────────────
            var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/passenger/route-requests");
            createReq.Headers.Add("X-User-Id", passengerId);
            createReq.Content = JsonContent.Create(new
            {
                start = new { lat = 52.2310, lng = 21.0140 },
                end = new { lat = 52.2480, lng = 21.0280 }
            });
            var createResp = await _client.SendAsync(createReq, cts.Token);
            var dto = await createResp.Content.ReadFromJsonAsync<PassengerRequestDto>(
                cancellationToken: cts.Token);
            Assert.NotNull(dto);
            var requestId = dto!.RequestId;

            // ── Step 3: Establish Passenger Listener ──────────────────────────────
            var passengerSseReq = new HttpRequestMessage(HttpMethod.Get,
                $"/api/passenger/route-requests/{requestId}/events");
            passengerSseReq.Headers.Add("X-User-Id", passengerId);
            passengerSseReq.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("text/event-stream"));
            var passengerSseResp = await _client.SendAsync(passengerSseReq,
                HttpCompletionOption.ResponseHeadersRead, cts.Token);

            var routesReady = await ReadNextSseEventAsync(passengerSseResp, "routes_ready",
                TimeSpan.FromSeconds(45), cts.Token);
            Assert.True(routesReady.Found, "Timed out waiting for routes_ready.");
            Assert.NotNull(routesReady.Data);

            var matchData = JsonDocument.Parse(routesReady.Data!);
            Assert.True(matchData.RootElement.TryGetProperty("Matches", out var matches), "routes_ready payload missing Matches.");
            Assert.True(matches.GetArrayLength() > 0, "Matches list returned empty.");

            var driverRouteId = matches[0].GetProperty("DriverRouteId").GetGuid();
            Assert.NotEqual(Guid.Empty, driverRouteId);

            // ── Step 4: Selection & Rejection Actions ─────────────────────────────
            var selectReq = new HttpRequestMessage(HttpMethod.Post,
                $"/api/passenger/route-requests/{requestId}/select");
            selectReq.Headers.Add("X-User-Id", passengerId);
            selectReq.Content = JsonContent.Create(new { driverRouteId });
            await _client.SendAsync(selectReq, cts.Token);

            var declineReq = new HttpRequestMessage(HttpMethod.Post,
                $"/api/driver/requests/{requestId}/confirmation");
            declineReq.Headers.Add("X-User-Id", driverId);
            declineReq.Content = JsonContent.Create(new { accepted = false });

            var declineResp = await _client.SendAsync(declineReq, cts.Token);
            Assert.Equal(HttpStatusCode.NoContent, declineResp.StatusCode);

            // ── Step 5: Verify Match Decline Stream Output ────────────────────────
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
                end = new { lat = end.Lat, lng = end.Lng }
            });
            var resp = await _client.SendAsync(req);
            var dto = await resp.Content.ReadFromJsonAsync<RegisterRouteDto>();
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
                end = new { lat = end.Lat, lng = end.Lng }
            });
            var resp = await _client.SendAsync(req);
            var dto = await resp.Content.ReadFromJsonAsync<PassengerRequestDto>();
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

                string? currentEventType = null;
                string? currentData = null;

                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync(cts.Token);
                    if (line is null) break;

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        if (currentEventType == targetEventType)
                        {
                            return (true, currentData);
                        }
                        currentEventType = null;
                        currentData = null;
                        continue;
                    }

                    if (line.StartsWith("event:"))
                    {
                        currentEventType = line["event:".Length..].Trim();
                    }
                    else if (line.StartsWith("data:"))
                    {
                        currentData = line["data:".Length..].Trim();
                    }
                }
            }
            catch (OperationCanceledException) { }

            return (false, null);
        }

        private record RegisterRouteDto(Guid JobId);
        private record PassengerRequestDto(Guid RequestId);
    }
}
