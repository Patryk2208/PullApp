using TripPlanner.Application.Repositories;
using TripPlanner.Domain.Compute;

namespace TripPlanner.Infrastructure.Fakes;

public class FakeRouteCalculator : IRouteCalculator
{
    private static readonly Dictionary<Guid, ComputeJobResult> _results = new();

    public Task<Guid> SendComputeAsync(ComputeJob job, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000, ct);

            ComputeJobResult result = job.JobType switch
            {
                JobType.DriverRoute => new DriverRouteComputeResult(
                    job.JobId,
                    new DriverRouteJobResult(
                        RouteGeomJson: """{"type":"LineString","coordinates":[[21.0122,52.2297],[21.0200,52.2350],[20.9752,52.2489]]}""",
                        EtaSeconds: 1800,
                        DistanceMeters: 12000)),

                JobType.PassengerMatch => new PassengerMatchComputeResult(
                    job.JobId,
                    new PassengerMatchJobResult([])),

                _ => new FailedComputeResult(job.JobId, job.JobType, "unknown_job_type"),
            };

            _results[job.JobId] = result;
        }, ct);

        return Task.FromResult(job.JobId);
    }

    public Task<ComputeJobResult?> TryGetResultAsync(Guid jobId, CancellationToken ct)
    {
        _results.TryGetValue(jobId, out var result);
        return Task.FromResult(result);
    }
}
