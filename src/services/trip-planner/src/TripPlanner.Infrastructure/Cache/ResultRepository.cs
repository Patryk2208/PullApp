using System.Text.Json;
using StackExchange.Redis;
using TripPlanner.Application.RouteCalculator;
using TripPlanner.Domain;

namespace TripPlanner.Infrastructure.Cache;

internal class RedisResultRepository : IResultRepository
{
    private readonly IDatabase _db;
    private readonly Options _options;

    public RedisResultRepository(IConnectionMultiplexer connectionMultiplexer)
    {
        _db = connectionMultiplexer.GetDatabase();
    }

    public async Task StoreResultAsync(ComputeResult result, CancellationToken ct)
    {
        var serialized = JsonSerializer.Serialize(result);
        var key = Guid.Empty;
        await _db.StringSetAsync("todo", new RedisValue(serialized)); 
    }

    public async Task<ComputeResult?> TryGetResultAsync(Guid id, CancellationToken ct)
    {
        var value = await _db.StringGetAsync("todo");
        if (value.IsNull)
        {
            return null;
        }
        return JsonSerializer.Deserialize<ComputeResult>(value.ToString());
    }
}