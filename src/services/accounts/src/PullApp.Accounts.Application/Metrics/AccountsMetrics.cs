using System.Diagnostics.Metrics;

namespace PullApp.Accounts.Application.Metrics;

public sealed class AccountsMetrics : IDisposable
{
    public const string MeterName = "Accounts";

    private readonly Meter _meter;
    private readonly Counter<long> _registrations;
    private readonly Counter<long> _loginSuccess;
    private readonly Counter<long> _loginFailed;
    private readonly Counter<long> _validationFailures;
    private readonly Histogram<double> _loginDuration;

    public AccountsMetrics()
    {
        _meter              = new Meter(MeterName);
        _registrations      = _meter.CreateCounter<long>("accounts.registrations",        "users");
        _loginSuccess       = _meter.CreateCounter<long>("accounts.login.success",        "attempts");
        _loginFailed        = _meter.CreateCounter<long>("accounts.login.failed",         "attempts");
        _validationFailures = _meter.CreateCounter<long>("accounts.validation.failures",  "failures");
        _loginDuration      = _meter.CreateHistogram<double>("accounts.login_duration_seconds", "s");
    }

    public void UserRegistered()      => _registrations.Add(1);
    public void LoginSucceeded()      => _loginSuccess.Add(1);
    public void LoginFailed()         => _loginFailed.Add(1);
    public void ValidationFailed()    => _validationFailures.Add(1);

    public void RecordLoginDuration(double seconds, bool success)
        => _loginDuration.Record(seconds, new KeyValuePair<string, object?>("result", success ? "success" : "failed"));

    public void Dispose() => _meter.Dispose();
}
