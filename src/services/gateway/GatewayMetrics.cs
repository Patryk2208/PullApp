using System.Diagnostics.Metrics;

public sealed class GatewayMetrics : IDisposable
{
    public const string MeterName = "Gateway";

    private readonly Meter _meter;
    private readonly Counter<long> _requests;
    private readonly Histogram<double> _duration;

    public GatewayMetrics()
    {
        _meter    = new Meter(MeterName);
        _requests = _meter.CreateCounter<long>("gateway_requests_total",            "requests");
        _duration = _meter.CreateHistogram<double>("gateway_request_duration_seconds", "s");

        foreach (var svc in new[] { "accounts", "trip-planner" })
        {
            foreach (var method in new[] { "GET", "POST", "PUT", "DELETE" })
            {
                foreach (var status in new[] { "200", "201", "400", "401", "403", "404", "500", "503" })
                    _requests.Add(0,
                        new KeyValuePair<string, object?>("service",     svc),
                        new KeyValuePair<string, object?>("method",      method),
                        new KeyValuePair<string, object?>("status_code", status));

                _duration.Record(0,
                    new KeyValuePair<string, object?>("service", svc),
                    new KeyValuePair<string, object?>("method",  method));
            }
        }
    }

    public void RecordRequest(string service, string method, int statusCode, double durationSeconds)
    {
        _requests.Add(1,
            new KeyValuePair<string, object?>("service",     service),
            new KeyValuePair<string, object?>("method",      method),
            new KeyValuePair<string, object?>("status_code", statusCode.ToString()));
        _duration.Record(durationSeconds,
            new KeyValuePair<string, object?>("service", service),
            new KeyValuePair<string, object?>("method",  method));
    }

    public void Dispose() => _meter.Dispose();
}
