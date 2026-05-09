using TripPlanner.Domain.Compute;

namespace TripPlanner.Domain.Passenger;

public enum RideRequestStatus
{
    Searching,
    RoutesPresented,
    PendingDriver,
    MatchConfirmed,
    Cancelled,
    NoMatch,
}

public class RideRequest
{
    public Guid Id { get; init; }
    public Guid PassengerId { get; init; }
    public RideRequestStatus Status { get; private set; } = RideRequestStatus.Searching;

    public GeoPoint StartPoint { get; init; } = default!;
    public GeoPoint EndPoint { get; init; } = default!;
    public MatchConstraints Constraints { get; init; } = new();

    // Ranked list delivered by Route-Calc; updated as drivers decline.
    public IReadOnlyList<MatchEntry>? MatchResults { get; private set; }

    // Set when the passenger picks a driver from the list.
    public Guid? SelectedRouteId { get; private set; }

    // References the active RouteJob for this request.
    public Guid? JobId { get; private set; }

    // Deadline stored in DB so ConfirmationMonitorWorker can query it without Redis.
    public DateTimeOffset? ConfirmationDeadline { get; private set; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }

    public void AssignJob(Guid jobId)
    {
        JobId = jobId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void PresentMatches(IReadOnlyList<MatchEntry> matches)
    {
        Status = RideRequestStatus.RoutesPresented;
        MatchResults = matches;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SelectRoute(Guid driverRouteId, DateTimeOffset confirmationDeadline)
    {
        Status = RideRequestStatus.PendingDriver;
        SelectedRouteId = driverRouteId;
        ConfirmationDeadline = confirmationDeadline;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Confirm()
    {
        Status = RideRequestStatus.MatchConfirmed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel()
    {
        Status = RideRequestStatus.Cancelled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkNoMatch()
    {
        Status = RideRequestStatus.NoMatch;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    // Returns true when the list is now empty and a re-search is required.
    public bool RemoveDriverFromResults(Guid driverRouteId)
    {
        if (MatchResults == null) return true;
        MatchResults = MatchResults.Where(m => m.DriverRouteId != driverRouteId).ToList();
        UpdatedAt = DateTimeOffset.UtcNow;
        return MatchResults.Count == 0;
    }

    // Resets back to Searching and clears results so a new job can be dispatched.
    public void ReSearch(Guid newJobId)
    {
        Status = RideRequestStatus.Searching;
        MatchResults = null;
        SelectedRouteId = null;
        ConfirmationDeadline = null;
        JobId = newJobId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
