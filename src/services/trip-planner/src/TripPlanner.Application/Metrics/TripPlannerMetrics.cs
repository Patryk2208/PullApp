using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace TripPlanner.Application.Metrics;

public sealed class TripPlannerMetrics : IDisposable
{
    public const string MeterName = "TripPlanner";

    private readonly Meter _meter;

    // ─── Matching pipeline ────────────────────────────────────────────────────
    private readonly Counter<long>   _matchingRequests;
    private readonly Counter<long>   _matchingResults;
    private readonly Counter<long>   _matchingNoDrivers;
    private readonly Histogram<double> _matchingQueueDuration;

    // ─── Match confirmation ───────────────────────────────────────────────────
    private readonly Counter<long>   _matchConfirmed;
    private readonly Counter<long>   _matchDeclined;
    private readonly Counter<long>   _driverDeclines;
    private readonly Histogram<double> _acceptanceDuration;

    // ─── Rides ────────────────────────────────────────────────────────────────
    private readonly Counter<long>     _ridesCompleted;
    private readonly Counter<long>     _ridesCancelled;
    private readonly Counter<long>     _rideTransitions;
    private readonly UpDownCounter<int> _rideActive;
    private readonly Histogram<double>  _rideStateDuration;

    // ─── Passenger flow ───────────────────────────────────────────────────────
    private readonly Counter<long> _routeRequestsCreated;
    private readonly Counter<long> _routeRequestsCancelled;
    private readonly Counter<long> _routesSelected;

    // ─── Driver route registrations ───────────────────────────────────────────
    private readonly Counter<long>     _driverRouteRegistrations;
    private readonly Histogram<double> _driverRouteRegistrationDuration;

    // ─── Compute queue / route-calc ───────────────────────────────────────────
    private readonly Counter<long>     _computeQueuePublish;
    private readonly Counter<long>     _computeResultsReceived;
    private readonly Histogram<double> _routeCalcDuration;

    // ─── In-memory timestamp tracking ─────────────────────────────────────────
    private readonly ConcurrentDictionary<Guid, long>               _matchingTimestamps    = new();
    private readonly ConcurrentDictionary<Guid, long>               _acceptanceTimestamps  = new();
    private readonly ConcurrentDictionary<Guid, long>               _rideStateTimestamps   = new();
    private readonly ConcurrentDictionary<Guid, (string JobType, long Ticks)> _routeCalcTimestamps = new();
    private readonly ConcurrentDictionary<Guid, long>               _driverRouteTimestamps = new();

    public TripPlannerMetrics()
    {
        _meter = new Meter(MeterName);

        _matchingRequests            = _meter.CreateCounter<long>("matching_requests_total",                        "requests");
        _matchingResults             = _meter.CreateCounter<long>("matching_result_total",                          "results");
        _matchingNoDrivers           = _meter.CreateCounter<long>("matching_no_drivers_found_total",                "occurrences");
        _matchingQueueDuration       = _meter.CreateHistogram<double>("matching_queue_duration_seconds",            "s");
        _matchConfirmed              = _meter.CreateCounter<long>("trip_planner.match.confirmed",                   "matches");
        _matchDeclined               = _meter.CreateCounter<long>("trip_planner.match.declined",                    "matches");
        _driverDeclines              = _meter.CreateCounter<long>("ride_driver_decline_total",                      "declines");
        _acceptanceDuration          = _meter.CreateHistogram<double>("ride_acceptance_duration_seconds",           "s");
        _ridesCompleted              = _meter.CreateCounter<long>("ride_completed_total",                           "rides");
        _ridesCancelled              = _meter.CreateCounter<long>("ride_cancelled_total",                           "rides");
        _rideTransitions             = _meter.CreateCounter<long>("ride_transitions_total",                         "transitions");
        _rideActive                  = _meter.CreateUpDownCounter<int>("ride_active",                               "rides");
        _rideStateDuration           = _meter.CreateHistogram<double>("ride_state_duration_seconds",                "s");
        _routeRequestsCreated        = _meter.CreateCounter<long>("trip_planner.requests.created",                  "requests");
        _routeRequestsCancelled      = _meter.CreateCounter<long>("trip_planner.requests.cancelled",                "requests");
        _routesSelected              = _meter.CreateCounter<long>("trip_planner.requests.route_selected",           "requests");
        _driverRouteRegistrations    = _meter.CreateCounter<long>("driver_route_registrations_total",               "routes");
        _driverRouteRegistrationDuration = _meter.CreateHistogram<double>("driver_route_registration_duration_seconds", "s");
        _computeQueuePublish         = _meter.CreateCounter<long>("compute_queue_publish_total",                    "jobs");
        _computeResultsReceived      = _meter.CreateCounter<long>("trip_planner.compute.results",                   "results");
        _routeCalcDuration           = _meter.CreateHistogram<double>("route_calc_duration_seconds",                "s");

        Initialize();
    }

    private void Initialize()
    {
        foreach (var status in new[] { "queued", "failed_validation", "no_area_coverage" })
            _matchingRequests.Add(0, T("status", status));

        foreach (var result in new[] { "matched", "no_drivers", "timeout", "error" })
        {
            _matchingResults.Add(0, T("result", result));
            _matchingQueueDuration.Record(0, T("result", result));
        }



        _matchingNoDrivers.Add(0);
        _acceptanceDuration.Record(0);
        _rideActive.Add(0);

        foreach (var reason in new[] { "explicit", "timeout", "no_response" })
            _driverDeclines.Add(0, T("reason", reason));

        foreach (var (from, to, reason) in new[]
        {
            ("pending_driver",     "searching",          "driver_declined"),
            ("pending_driver",     "pickup",             "driver_accepted"),
            ("pickup",             "awaiting_passenger", "driver_arrived"),
            ("awaiting_passenger", "in_ride",            "driver_started"),
            ("awaiting_passenger", "in_ride",            "passenger_started"),
            ("in_ride",            "completed",          "normal"),
            ("in_ride",            "cancelled",          "driver_cancel"),
            ("in_ride",            "cancelled",          "passenger_cancel"),
            ("pre_pickup",         "cancelled",          "driver_cancel"),
            ("pre_pickup",         "cancelled",          "passenger_cancel"),
        })
            _rideTransitions.Add(0, T("from_state", from), T("to_state", to), T("reason", reason));

        foreach (var (from, to) in new[]
        {
            ("pickup",             "awaiting_passenger"),
            ("awaiting_passenger", "in_ride"),
            ("in_ride",            "completed"),
            ("in_ride",            "cancelled"),
            ("pre_pickup",         "cancelled"),
        })
            _rideStateDuration.Record(0, T("from_state", from), T("to_state", to));

        foreach (var type in new[] { "normal", "early_termination" })
            _ridesCompleted.Add(0, T("completion_type", type));

        foreach (var (by, stage) in new[]
        {
            ("driver",    "after_match"),
            ("driver",    "during_ride"),
            ("passenger", "after_match"),
            ("passenger", "during_ride"),
            ("system",    "before_match"),
            ("system",    "after_match"),
            ("system",    "during_ride"),
        })
            _ridesCancelled.Add(0, T("cancelled_by", by), T("stage", stage));

        foreach (var status in new[] { "queued", "completed", "failed" })
            _driverRouteRegistrations.Add(0, T("status", status));

        foreach (var result in new[] { "success", "error" })
        {
            _driverRouteRegistrationDuration.Record(0, T("result", result));
            foreach (var jobType in new[] { "route_registration", "passenger_match" })
                _routeCalcDuration.Record(0, T("job_type", jobType), T("result", result));
        }

        foreach (var (jobType, status) in new[]
        {
            ("route_registration", "success"), ("route_registration", "failed"),
            ("passenger_match",    "success"), ("passenger_match",    "failed"),
        })
            _computeQueuePublish.Add(0, T("job_type", jobType), T("status", status));

        _matchConfirmed.Add(0);
        _matchDeclined.Add(0);
        _routeRequestsCreated.Add(0);
        _routeRequestsCancelled.Add(0);
        _routesSelected.Add(0);
        _computeResultsReceived.Add(0);
    }

    // ─── Matching pipeline ────────────────────────────────────────────────────

    public void MatchingRequestRecorded(string status)
        => _matchingRequests.Add(1, T("status", status));

    public void MatchingResultRecorded(string result)
        => _matchingResults.Add(1, T("result", result));

    public void MatchingNoDriversFound()
        => _matchingNoDrivers.Add(1);

    // ─── Match confirmation ───────────────────────────────────────────────────

    public void MatchConfirmed()             => _matchConfirmed.Add(1);
    public void MatchDeclined()              => _matchDeclined.Add(1);
    public void DriverDeclined(string reason) => _driverDeclines.Add(1, T("reason", reason));

    // ─── Ride lifecycle ───────────────────────────────────────────────────────

    public void RideCompleted(string completionType = "normal")
        => _ridesCompleted.Add(1, T("completion_type", completionType));

    public void RideCancelled(string by, string stage)
        => _ridesCancelled.Add(1, T("cancelled_by", by), T("stage", stage));

    // Parameterless form for request-level transitions (no ride object, no duration tracking).
    public void RideTransition(string fromState, string toState, string reason)
        => _rideTransitions.Add(1, T("from_state", fromState), T("to_state", toState), T("reason", reason));

    // Ride-object form: also records ride_state_duration_seconds and advances the state clock.
    public void RideTransition(Guid rideId, string fromState, string toState, string reason)
    {
        _rideTransitions.Add(1, T("from_state", fromState), T("to_state", toState), T("reason", reason));

        if (_rideStateTimestamps.TryRemove(rideId, out var startTicks))
        {
            var seconds = TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks - startTicks).TotalSeconds;
            _rideStateDuration.Record(seconds, T("from_state", fromState), T("to_state", toState));
        }

        if (toState is not ("completed" or "cancelled"))
            _rideStateTimestamps[rideId] = DateTimeOffset.UtcNow.Ticks;
    }

    // Call once when a ride is first created (enters its first state).
    public void RecordRideStateEntered(Guid rideId)
        => _rideStateTimestamps[rideId] = DateTimeOffset.UtcNow.Ticks;

    public void RideActiveAdd(int delta) => _rideActive.Add(delta);

    // ─── Passenger flow ───────────────────────────────────────────────────────

    public void RouteRequestCreated()   => _routeRequestsCreated.Add(1);
    public void RouteRequestCancelled() => _routeRequestsCancelled.Add(1);
    public void RouteSelected()         => _routesSelected.Add(1);

    // ─── Driver route registrations ───────────────────────────────────────────

    public void DriverRouteQueued() => _driverRouteRegistrations.Add(1, T("status", "queued"));

    public void DriverRouteCompleted(Guid correlationId)
    {
        _driverRouteRegistrations.Add(1, T("status", "completed"));
        RecordDriverRouteDuration(correlationId, "success");
    }

    public void DriverRouteFailed(Guid correlationId)
    {
        _driverRouteRegistrations.Add(1, T("status", "failed"));
        RecordDriverRouteDuration(correlationId, "error");
    }

    private void RecordDriverRouteDuration(Guid correlationId, string result)
    {
        if (!_driverRouteTimestamps.TryRemove(correlationId, out var startTicks)) return;
        var seconds = TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks - startTicks).TotalSeconds;
        _driverRouteRegistrationDuration.Record(seconds, T("result", result));
    }

    // ─── Compute queue ────────────────────────────────────────────────────────

    public void ComputeJobPublished(string jobType, string status = "success")
        => _computeQueuePublish.Add(1, T("job_type", jobType), T("status", status));

    public void ComputeResultReceived() => _computeResultsReceived.Add(1);

    // ─── Latency tracking ─────────────────────────────────────────────────────

    public void RecordMatchingJobPublished(Guid jobId)
        => _matchingTimestamps[jobId] = DateTimeOffset.UtcNow.Ticks;

    public void RecordMatchingJobResult(Guid jobId, string result)
    {
        if (!_matchingTimestamps.TryRemove(jobId, out var startTicks)) return;
        var seconds = TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks - startTicks).TotalSeconds;
        _matchingQueueDuration.Record(seconds, T("result", result));
    }

    public void RecordAcceptanceStarted(Guid requestId)
        => _acceptanceTimestamps[requestId] = DateTimeOffset.UtcNow.Ticks;

    public void RecordAcceptanceEnded(Guid requestId)
    {
        if (!_acceptanceTimestamps.TryRemove(requestId, out var startTicks)) return;
        var seconds = TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks - startTicks).TotalSeconds;
        _acceptanceDuration.Record(seconds);
    }

    // Tracks route_calc_duration_seconds for all job types.
    // For route_registration jobs, also starts the driver_route_registration_duration_seconds clock.
    public void RecordRouteCalcPublished(Guid jobId, string jobType)
    {
        _routeCalcTimestamps[jobId] = (jobType, DateTimeOffset.UtcNow.Ticks);
        if (jobType == "route_registration")
            _driverRouteTimestamps[jobId] = DateTimeOffset.UtcNow.Ticks;
    }

    public void RecordRouteCalcResult(Guid jobId, string result)
    {
        if (!_routeCalcTimestamps.TryRemove(jobId, out var entry)) return;
        var seconds = TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks - entry.Ticks).TotalSeconds;
        _routeCalcDuration.Record(seconds, T("job_type", entry.JobType), T("result", result));
    }

    public void Dispose() => _meter.Dispose();

    private static KeyValuePair<string, object?> T(string key, object? value) => new(key, value);
}
