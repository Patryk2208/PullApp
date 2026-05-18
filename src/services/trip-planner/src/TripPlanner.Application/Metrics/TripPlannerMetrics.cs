using System.Diagnostics.Metrics;

namespace TripPlanner.Application.Metrics;

public sealed class TripPlannerMetrics : IDisposable
{
    public const string MeterName = "TripPlanner";

    private readonly Meter _meter;

    // Driver
    private readonly Counter<long> _routesRegistered;
    private readonly Counter<long> _routesModified;
    private readonly Counter<long> _routesCancelled;

    // Matching
    private readonly Counter<long> _matchConfirmed;
    private readonly Counter<long> _matchDeclined;

    // Rides
    private readonly Counter<long> _ridesCompleted;
    private readonly Counter<long> _ridesCancelled;

    // Passenger
    private readonly Counter<long> _routeRequestsCreated;
    private readonly Counter<long> _routeRequestsCancelled;
    private readonly Counter<long> _routesSelected;

    // Compute
    private readonly Counter<long> _computeResultsReceived;

    public TripPlannerMetrics()
    {
        _meter = new Meter(MeterName);

        _routesRegistered      = _meter.CreateCounter<long>("trip_planner.routes.registered",      "routes");
        _routesModified        = _meter.CreateCounter<long>("trip_planner.routes.modified",        "routes");
        _routesCancelled       = _meter.CreateCounter<long>("trip_planner.routes.cancelled",       "routes");
        _matchConfirmed        = _meter.CreateCounter<long>("trip_planner.match.confirmed",        "matches");
        _matchDeclined         = _meter.CreateCounter<long>("trip_planner.match.declined",         "matches");
        _ridesCompleted        = _meter.CreateCounter<long>("trip_planner.rides.completed",        "rides");
        _ridesCancelled        = _meter.CreateCounter<long>("trip_planner.rides.cancelled",        "rides");
        _routeRequestsCreated  = _meter.CreateCounter<long>("trip_planner.requests.created",       "requests");
        _routeRequestsCancelled= _meter.CreateCounter<long>("trip_planner.requests.cancelled",     "requests");
        _routesSelected        = _meter.CreateCounter<long>("trip_planner.requests.route_selected","requests");
        _computeResultsReceived= _meter.CreateCounter<long>("trip_planner.compute.results",        "results");
    }

    public void RouteRegistered()       => _routesRegistered.Add(1);
    public void RouteModified()         => _routesModified.Add(1);
    public void RouteCancelled()        => _routesCancelled.Add(1);
    public void MatchConfirmed()        => _matchConfirmed.Add(1);
    public void MatchDeclined()         => _matchDeclined.Add(1);
    public void RideCompleted()         => _ridesCompleted.Add(1);
    public void RideCancelled(string by)=> _ridesCancelled.Add(1, new KeyValuePair<string, object?>("cancelled_by", by));
    public void RouteRequestCreated()   => _routeRequestsCreated.Add(1);
    public void RouteRequestCancelled() => _routeRequestsCancelled.Add(1);
    public void RouteSelected()         => _routesSelected.Add(1);
    public void ComputeResultReceived() => _computeResultsReceived.Add(1);

    public void Dispose() => _meter.Dispose();
}
