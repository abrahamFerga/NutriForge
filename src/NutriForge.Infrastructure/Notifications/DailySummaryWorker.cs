using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NutriForge.Application.Notifications;
using NutriForge.Infrastructure.Persistence;

namespace NutriForge.Infrastructure.Notifications;

/// <summary>
/// Sends each enabled+linked subscriber their daily calorie summary at their chosen UTC hour, at most
/// once a day (deduped by <c>LastSentOn</c>). Runs as "system": it bypasses the per-user query filter
/// and scopes every read explicitly to the subscriber's id — the only safe way for a background job to
/// build other users' summaries. Message text is the SAME formatter as the on-demand endpoint.
/// </summary>
public sealed partial class DailySummaryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<DailySummaryWorker> logger) : BackgroundService
{
    // Tick often enough to catch the target hour; LastSentOn makes repeats within the hour a no-op.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NutriForgeDbContext>();
                var channel = scope.ServiceProvider.GetRequiredService<INotificationChannel>();
                await ProcessDueAsync(db, channel, DateTimeOffset.UtcNow, logger, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // a worker must not die on one bad tick
            catch (Exception ex)
            {
                LogTickFailed(logger, ex);
            }
#pragma warning restore CA1031
        }
    }

    /// <summary>
    /// Send to every subscription due at <paramref name="now"/>'s UTC hour that hasn't gone out today.
    /// Testable in isolation: pass a context, a channel, and a fixed clock.
    /// </summary>
    public static async Task<int> ProcessDueAsync(
        NutriForgeDbContext db, INotificationChannel channel, DateTimeOffset now, ILogger logger, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(channel);

        if (!channel.IsConfigured)
        {
            return 0;
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var hour = now.UtcDateTime.Hour;

        // IgnoreQueryFilters: the system context owns no user, so the per-user filter would hide everyone.
        var due = await db.ChannelSubscriptions.IgnoreQueryFilters()
            .Where(s => s.Enabled && s.Address != null && s.SendHourUtc == hour
                && (s.LastSentOn == null || s.LastSentOn < today))
            .ToListAsync(ct).ConfigureAwait(false);

        var sent = 0;
        foreach (var sub in due)
        {
            try
            {
                var totals = await db.DiaryEntries.IgnoreQueryFilters()
                    .Where(e => e.UserId == sub.UserId && e.Date == today)
                    .GroupBy(_ => 1)
                    .Select(g => new { Kcal = g.Sum(x => x.Kcal), Protein = g.Sum(x => x.ProteinG) })
                    .FirstOrDefaultAsync(ct).ConfigureAwait(false);
                var targetKcal = await db.Targets.IgnoreQueryFilters()
                    .Where(t => t.UserId == sub.UserId)
                    .Select(t => (double?)t.Kcal)
                    .FirstOrDefaultAsync(ct).ConfigureAwait(false);

                var message = DailySummaryService.Format(today, totals?.Kcal ?? 0, totals?.Protein ?? 0, targetKcal);

                // Mark sent BEFORE the channel save so both persist in the channel's single SaveChanges
                // (same scoped context); the dedup holds even if the same tick is retried.
                sub.LastSentOn = today;
                if (await channel.SendAsync(sub.UserId, message, ct).ConfigureAwait(false))
                {
                    sent++;
                }
            }
#pragma warning disable CA1031 // one subscriber's failure must not stop the rest
            catch (Exception ex)
            {
                LogSubscriberFailed(logger, sub.UserId, ex);
            }
#pragma warning restore CA1031
        }

        // Persist any LastSentOn that the channel didn't already flush (e.g. a channel that doesn't save).
        if (due.Count > 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return sent;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Daily-summary tick failed; will retry.")]
    private static partial void LogTickFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Daily summary failed for subscriber {UserId}.")]
    private static partial void LogSubscriberFailed(ILogger logger, Guid userId, Exception ex);
}
