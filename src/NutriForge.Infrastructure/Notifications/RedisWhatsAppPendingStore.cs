using System.Text.Json;
using Microsoft.Extensions.Logging;
using NutriForge.Application.Notifications;
using StackExchange.Redis;

namespace NutriForge.Infrastructure.Notifications;

/// <summary>
/// Redis-backed implementation of <see cref="IWhatsAppPendingStore"/>.
/// All keys degrade gracefully on Redis failure — faults are logged and swallowed so a
/// Redis outage never crashes the inbound webhook.
/// </summary>
public sealed partial class RedisWhatsAppPendingStore(
    IConnectionMultiplexer redis,
    ILogger<RedisWhatsAppPendingStore> logger) : IWhatsAppPendingStore
{
    private static readonly TimeSpan SeenTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan PendingTtl = TimeSpan.FromMinutes(15);

    private static string SeenKey(string messageSid) => $"wa:msg:{messageSid}";
    private static string PendingKey(string from) => $"wa:pending:{from}";

    public async Task<bool> IsSeenAsync(string messageSid, CancellationToken ct = default)
    {
        try
        {
            return await redis.GetDatabase().KeyExistsAsync(SeenKey(messageSid)).ConfigureAwait(false);
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            LogFault(logger, "IsSeenAsync", ex);
            return false; // on failure, allow the message through (better than silently dropping)
        }
#pragma warning restore CA1031
    }

    public async Task MarkSeenAsync(string messageSid, CancellationToken ct = default)
    {
        try
        {
            await redis.GetDatabase()
                .StringSetAsync(SeenKey(messageSid), "1", SeenTtl)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031
        catch (Exception ex) { LogFault(logger, "MarkSeenAsync", ex); }
#pragma warning restore CA1031
    }

    public async Task<WhatsAppPending?> GetPendingAsync(string from, CancellationToken ct = default)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(PendingKey(from)).ConfigureAwait(false);
            if (value.IsNullOrEmpty)
            {
                return null;
            }

            return JsonSerializer.Deserialize<WhatsAppPending>(value.ToString());
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            LogFault(logger, "GetPendingAsync", ex);
            return null;
        }
#pragma warning restore CA1031
    }

    public async Task SetPendingAsync(string from, WhatsAppPending pending, CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(pending);
            await redis.GetDatabase()
                .StringSetAsync(PendingKey(from), json, PendingTtl)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031
        catch (Exception ex) { LogFault(logger, "SetPendingAsync", ex); }
#pragma warning restore CA1031
    }

    public async Task ClearPendingAsync(string from, CancellationToken ct = default)
    {
        try
        {
            await redis.GetDatabase().KeyDeleteAsync(PendingKey(from)).ConfigureAwait(false);
        }
#pragma warning disable CA1031
        catch (Exception ex) { LogFault(logger, "ClearPendingAsync", ex); }
#pragma warning restore CA1031
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "WhatsApp pending store {Operation} failed; degrading gracefully.")]
    private static partial void LogFault(ILogger logger, string operation, Exception ex);
}
