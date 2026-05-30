using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace TripPlanner.Application.Metrics;

public sealed class TripPlannerMetrics : IDisposable
{
    public const string MeterName = "TripPlanner";

    private readonly Meter _meter;

    private readonly Counter<long>      _matchingRequests;
    private readonly Histogram<double>  _matchingQueueDuration;
    private readonly Counter<long>      _matchingResults;

    private readonly Counter<long>      _rideTransitions;
    private readonly UpDownCounter<int> _rideActive;
    private readonly Counter<long>      _rideCancelled;
    private readonly Counter<long>      _driverDeclines;

    private readonly Counter<long>     _driverRouteRegistrations;
    private readonly Histogram<double> _routeCalcDuration;

    private readonly ConcurrentDictionary<Guid, long>                        _matchingTimestamps  = new();
    private readonly ConcurrentDictionary<Guid, (string JobType, long Ticks)> _routeCalcTimestamps = new();

    public TripPlannerMetrics()
    {
        _meter = new Meter(MeterName);

        _matchingRequests      = _meter.CreateCounter<long>("matching_requests_total",          "requests");
        _matchingQueueDuration = _meter.CreateHistogram<double>("matching_queue_duration_seconds", "s");
        _matchingResults       = _meter.CreateCounter<long>("matching_result_total",            "results");

        _rideTransitions = _meter.CreateCounter<long>("ride_transitions_total",    "transitions");
        _rideActive      = _meter.CreateUpDownCounter<int>("ride_active",          "rides");
        _rideCancelled   = _meter.CreateCounter<long>("ride_cancelled_total",      "rides");
        _driverDeclines  = _meter.CreateCounter<long>("ride_driver_decline_total", "declines");

        _driverRouteRegistrations = _meter.CreateCounter<long>("driver_route_registrations_total", "routes");
        _routeCalcDuration        = _meter.CreateHistogram<double>("route_calc_duration_seconds",  "s");
    }

    // ─── Matching ─────────────────────────────────────────────────────────────

    public void MatchingRequestRecorded(string status)
        => _matchingRequests.Add(1, T("status", status));

    public void RecordMatchingJobPublished(Guid jobId)
        => _matchingTimestamps[jobId] = DateTimeOffset.UtcNow.Ticks;

    public void RecordMatchingJobResult(Guid jobId, string result)
    {
        _matchingResults.Add(1, T("result", result));
        if (_matchingTimestamps.TryRemove(jobId, out var startTicks))
            _matchingQueueDuration.Record(
                TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks - startTicks).TotalSeconds,
                T("result", result));
    }

    // ─── Ride lifecycle ───────────────────────────────────────────────────────

    public void RideTransition(string fromState, string toState, string reason)
        => _rideTransitions.Add(1, T("from_state", fromState), T("to_state", toState), T("reason", reason));

    public void RideActiveAdd(int delta) => _rideActive.Add(delta);

    public void RideCancelled(string by, string stage)
        => _rideCancelled.Add(1, T("cancelled_by", by), T("stage", stage));

    public void DriverDeclined(string reason)
        => _driverDeclines.Add(1, T("reason", reason));

    // ─── Driver route registrations ───────────────────────────────────────────

    public void DriverRouteRegistrationQueued()
        => _driverRouteRegistrations.Add(1, T("status", "queued"));

    public void DriverRouteRegistrationCompleted()
        => _driverRouteRegistrations.Add(1, T("status", "completed"));

    public void DriverRouteRegistrationFailed()
        => _driverRouteRegistrations.Add(1, T("status", "failed"));

    // ─── Route-calc duration ──────────────────────────────────────────────────

    public void RecordRouteCalcPublished(Guid jobId, string jobType)
        => _routeCalcTimestamps[jobId] = (jobType, DateTimeOffset.UtcNow.Ticks);

    public void RecordRouteCalcResult(Guid jobId, string result)
    {
        if (_routeCalcTimestamps.TryRemove(jobId, out var entry))
            _routeCalcDuration.Record(
                TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks - entry.Ticks).TotalSeconds,
                T("job_type", entry.JobType), T("result", result));
    }

    public void Dispose() => _meter.Dispose();

    private static KeyValuePair<string, object?> T(string key, object? value) => new(key, value);
}
