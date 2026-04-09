using StackExchange.Redis;
using TripPlanner.Application.RouteCalculator;
using TripPlanner.Domain;

namespace TripPlanner.Infrastructure.Cache;

internal class ResultRepository : IResultRepository
{
    private readonly IDatabase _db;
    private readonly Options _options;

    public ResultRepository(IConnectionMultiplexer connectionMultiplexer)
    {
        _db = connectionMultiplexer.GetDatabase();
    }

    public Task StoreResultAsync(ComputeResult result, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    public Task<ComputeResult?> TryGetResultAsync(Guid id, CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}