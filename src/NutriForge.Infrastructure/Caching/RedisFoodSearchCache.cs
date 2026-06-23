using System.Text.Json;
using Microsoft.Extensions.Logging;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Food;
using StackExchange.Redis;

namespace NutriForge.Infrastructure.Caching;

/// <summary>
/// Redis-backed food-search cache (short TTL). Degrades gracefully: any Redis fault logs and
/// falls through to the catalog rather than failing the request.
/// </summary>
public sealed partial class RedisFoodSearchCache(IConnectionMultiplexer redis, ILogger<RedisFoodSearchCache> logger)
    : IFoodSearchCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private static string Key(string normalizedQuery) => $"food-search:{normalizedQuery}";

    public async Task<IReadOnlyList<FoodSummary>?> GetAsync(string normalizedQuery, CancellationToken ct = default)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(Key(normalizedQuery)).ConfigureAwait(false);
            return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<List<FoodSummary>>(value.ToString());
        }
#pragma warning disable CA1031 // cache is best-effort
        catch (Exception ex)
        {
            LogCacheFault(logger, "get", ex);
            return null;
        }
#pragma warning restore CA1031
    }

    public async Task SetAsync(string normalizedQuery, IReadOnlyList<FoodSummary> results, CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(results);
            await redis.GetDatabase().StringSetAsync(Key(normalizedQuery), json, Ttl).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // cache is best-effort
        catch (Exception ex)
        {
            LogCacheFault(logger, "set", ex);
        }
#pragma warning restore CA1031
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Food-search cache {Operation} failed; bypassing cache.")]
    private static partial void LogCacheFault(ILogger logger, string operation, Exception ex);
}
