using System.Text.Json;
using StackExchange.Redis;
using TripPlanner.Application.Repositories;
using TripPlanner.Domain.Compute;

namespace TripPlanner.Infrastructure.Cache;

public class RedisResultRepository(IConnectionMultiplexer redis, RedisOptions options) : IResultRepository
{
    private readonly IDatabase _db = redis.GetDatabase();
    private const string Prefix = "compute-results";

    private string Key(Guid id) => $"{options.KeyPrefix}:{Prefix}:{id}";

    public async Task StoreResultAsync(ComputeJobResult result, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(result, _jsonOptions);
        await _db.StringSetAsync(Key(result.JobId), json);
    }

    public async Task<ComputeJobResult?> TryGetResultAsync(Guid jobId, CancellationToken ct)
    {
        var value = await _db.StringGetAsync(Key(jobId));
        return value.IsNull ? null : JsonSerializer.Deserialize<ComputeJobResult>(value.ToString(), _jsonOptions);
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        // Preserve discriminator so polymorphic records round-trip correctly.
        WriteIndented = false,
    };
}
