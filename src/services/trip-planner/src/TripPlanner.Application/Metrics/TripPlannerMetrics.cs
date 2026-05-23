using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace TripPlanner.Application.Metrics;

public sealed class TripPlannerMetrics : IDisposable
{
    public const string MeterName = "TripPlanner";

    private readonly Meter _meter;

    // ─── Driver ───────────────────────────────────────────────────────────────
    private readonly Counter<long> _routesRegistered;
    private readonly Counter<long> _routesModified;
    private readonly Counter<long> _routesCancelled;

    // ─── Matching ─────────────────────────────────────────────────────────────
    private readonly Counter<long> _matchConfirmed;
    private readonly Counter<long> _matchDeclined;
    private readonly Counter<long> _matchingRequests;
    private readonly Counter<long> _matchingResults;
    private readonly Counter<long> _matchingNoDrivers;
    private readonly Counter<long> _driverDeclines;
    private readonly Histogram<double> _matchingQueueDuration;
    private readonly Histogram<double> _acceptanceDuration;

    // ─── Rides ────────────────────────────────────────────────────────────────
    private readonly Counter<long> _ridesCompleted;
    private readonly Counter<long> _ridesCancelled;
    private readonly Counter<long> _rideTransitions;
    private readonly UpDownCounter<int> _rideActive;

    // ─── Passenger ────────────────────────────────────────────────────────────
    private readonly Counter<long> _routeRequestsCreated;
    private readonly Counter<long> _routeRequestsCancelled;
    private readonly Counter<long> _routesSelected;

    // ─── Compute ──────────────────────────────────────────────────────────────
    private readonly Counter<long> _computeResultsReceived;

    // ─── In-memory timestamp tracking ─────────────────────────────────────────
    private readonly ConcurrentDictionary<Guid, long> _matchingTimestamps = new();
    private readonly ConcurrentDictionary<Guid, long> _acceptanceTimestamps = new();

    public TripPlannerMetrics()
    {
        _meter = new Meter(MeterName);

        _routesRegistered      = _meter.CreateCounter<long>("trip_planner.routes.registered",       "routes");
        _routesModified        = _meter.CreateCounter<long>("trip_planner.routes.modified",         "routes");
        _routesCancelled       = _meter.CreateCounter<long>("trip_planner.routes.cancelled",        "routes");
        _matchConfirmed        = _meter.CreateCounter<long>("trip_planner.match.confirmed",         "matches");
        _matchDeclined         = _meter.CreateCounter<long>("trip_planner.match.declined",          "matches");
        _matchingRequests      = _meter.CreateCounter<long>("matching_requests_total",              "requests");
        _matchingResults       = _meter.CreateCounter<long>("matching_result_total",                "results");
        _matchingNoDrivers     = _meter.CreateCounter<long>("matching_no_drivers_found_total",      "occurrences");
        _driverDeclines        = _meter.CreateCounter<long>("ride_driver_decline_total",            "declines");
        _matchingQueueDuration = _meter.CreateHistogram<double>("matching_queue_duration_seconds",  "s");
        _acceptanceDuration    = _meter.CreateHistogram<double>("ride_acceptance_duration_seconds", "s");
        _ridesCompleted        = _meter.CreateCounter<long>("trip_planner.rides.completed",         "rides");
        _ridesCancelled        = _meter.CreateCounter<long>("trip_planner.rides.cancelled",         "rides");
        _rideTransitions       = _meter.CreateCounter<long>("ride_transitions_total",               "transitions");
        _rideActive            = _meter.CreateUpDownCounter<int>("ride_active",                     "rides");
        _routeRequestsCreated  = _meter.CreateCounter<long>("trip_planner.requests.created",        "requests");
        _routeRequestsCancelled= _meter.CreateCounter<long>("trip_planner.requests.cancelled",      "requests");
        _routesSelected        = _meter.CreateCounter<long>("trip_planner.requests.route_selected", "requests");
        _computeResultsReceived= _meter.CreateCounter<long>("trip_planner.compute.results",         "results");

        Initialize();
    }

    private void Initialize()
    {
        foreach (var status in new[] { "queued", "failed_validation", "no_area_coverage" })
            _matchingRequests.Add(0, new KeyValuePair<string, object?>("status", status));

        foreach (var result in new[] { "matched", "no_drivers", "timeout", "error" })
        {
            _matchingResults.Add(0, new KeyValuePair<string, object?>("result", result));
            _matchingQueueDuration.Record(0, new KeyValuePair<string, object?>("result", result));
        }

        _matchingNoDrivers.Add(0);
        _acceptanceDuration.Record(0);
        _rideActive.Add(0);

        foreach (var reason in new[] { "explicit", "timeout" })
            _driverDeclines.Add(0, new KeyValuePair<string, object?>("reason", reason));

        foreach (var (from, to, reason) in new[]
        {
            ("pending_driver",    "searching",        "driver_declined"),
            ("pending_driver",    "pickup",           "driver_accepted"),
            ("pickup",            "awaiting_passenger","driver_arrived"),
            ("awaiting_passenger","in_ride",          "driver_started"),
            ("awaiting_passenger","in_ride",          "passenger_started"),
            ("in_ride",           "completed",        "normal"),
            ("in_ride",           "cancelled",        "driver_cancel"),
            ("in_ride",           "cancelled",        "passenger_cancel"),
            ("pre_pickup",        "cancelled",        "driver_cancel"),
            ("pre_pickup",        "cancelled",        "passenger_cancel"),
        })
            _rideTransitions.Add(0,
                new KeyValuePair<string, object?>("from_state", from),
                new KeyValuePair<string, object?>("to_state",   to),
                new KeyValuePair<string, object?>("reason",     reason));

        foreach (var by in new[] { "driver", "passenger" })
            _ridesCancelled.Add(0, new KeyValuePair<string, object?>("cancelled_by", by));

        _routesRegistered.Add(0);
        _routesModified.Add(0);
        _routesCancelled.Add(0);
        _matchConfirmed.Add(0);
        _matchDeclined.Add(0);
        _ridesCompleted.Add(0);
        _routeRequestsCreated.Add(0);
        _routeRequestsCancelled.Add(0);
        _routesSelected.Add(0);
        _computeResultsReceived.Add(0);
    }

    // ─── Existing ─────────────────────────────────────────────────────────────
    public void RouteRegistered()        => _routesRegistered.Add(1);
    public void RouteModified()          => _routesModified.Add(1);
    public void RouteCancelled()         => _routesCancelled.Add(1);
    public void MatchConfirmed()         => _matchConfirmed.Add(1);
    public void MatchDeclined()          => _matchDeclined.Add(1);
    public void RideCompleted()          => _ridesCompleted.Add(1);
    public void RideCancelled(string by) => _ridesCancelled.Add(1, new KeyValuePair<string, object?>("cancelled_by", by));
    public void RouteRequestCreated()    => _routeRequestsCreated.Add(1);
    public void RouteRequestCancelled()  => _routeRequestsCancelled.Add(1);
    public void RouteSelected()          => _routesSelected.Add(1);
    public void ComputeResultReceived()  => _computeResultsReceived.Add(1);

    // ─── Matching pipeline ────────────────────────────────────────────────────
    public void MatchingRequestRecorded(string status)
        => _matchingRequests.Add(1, new KeyValuePair<string, object?>("status", status));

    public void MatchingResultRecorded(string result)
        => _matchingResults.Add(1, new KeyValuePair<string, object?>("result", result));

    public void MatchingNoDriversFound()
        => _matchingNoDrivers.Add(1);

    public void DriverDeclined(string reason)
        => _driverDeclines.Add(1, new KeyValuePair<string, object?>("reason", reason));

    // ─── Ride lifecycle ───────────────────────────────────────────────────────
    public void RideTransition(string fromState, string toState, string reason)
        => _rideTransitions.Add(1,
            new KeyValuePair<string, object?>("from_state", fromState),
            new KeyValuePair<string, object?>("to_state",   toState),
            new KeyValuePair<string, object?>("reason",     reason));

    public void RideActiveAdd(int delta)
        => _rideActive.Add(delta);

    // ─── Latency tracking (in-memory) ─────────────────────────────────────────
    public void RecordMatchingJobPublished(Guid jobId)
        => _matchingTimestamps[jobId] = DateTimeOffset.UtcNow.Ticks;

    public void RecordMatchingJobResult(Guid jobId, string result)
    {
        if (!_matchingTimestamps.TryRemove(jobId, out var startTicks)) return;
        var seconds = TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks - startTicks).TotalSeconds;
        _matchingQueueDuration.Record(seconds, new KeyValuePair<string, object?>("result", result));
    }

    public void RecordAcceptanceStarted(Guid requestId)
        => _acceptanceTimestamps[requestId] = DateTimeOffset.UtcNow.Ticks;

    public void RecordAcceptanceEnded(Guid requestId)
    {
        if (!_acceptanceTimestamps.TryRemove(requestId, out var startTicks)) return;
        var seconds = TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks - startTicks).TotalSeconds;
        _acceptanceDuration.Record(seconds);
    }

    public void Dispose() => _meter.Dispose();
}
