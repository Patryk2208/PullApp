using System.Text.Json;
using StackExchange.Redis;
using TripPlanner.Application.RouteCalculator;
using TripPlanner.Domain;
using TripPlanner.Domain.Compute;

namespace TripPlanner.Infrastructure.Cache;

internal class RedisResultRepository(IConnectionMultiplexer connectionMultiplexer, RedisOptions options)
    : IResultRepository
{
    private readonly IDatabase _db = connectionMultiplexer.GetDatabase();
    public const string Name = "compute-results";

    private string _generateKey(Guid id) => $"{options.KeyPrefix}:{Name}:{id}";

    public async Task StoreResultAsync(ComputeResult result, CancellationToken ct)
    {
        var serialized = JsonSerializer.Serialize(result);
        var key = Guid.Empty;
        await _db.StringSetAsync(_generateKey(key), new RedisValue(serialized)); 
    }

    public async Task<ComputeResult?> TryGetResultAsync(Guid id, CancellationToken ct)
    {
        var value = await _db.StringGetAsync(_generateKey(id));
        if (value.IsNull)
        {
            return null;
        }
        return JsonSerializer.Deserialize<ComputeResult>(value.ToString());
    }
}