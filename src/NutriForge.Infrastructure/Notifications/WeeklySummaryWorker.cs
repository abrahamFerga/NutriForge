using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NutriForge.Application.Notifications;
using NutriForge.Infrastructure.Persistence;

namespace NutriForge.Infrastructure.Notifications;

/// <summary>
/// Sends a 7-day progress digest to all subscribers who have opted in to WeeklySummaryEnabled,
/// are linked, and haven't received this Sunday's digest yet. Runs on the same 5-minute poll as
/// the daily worker; the Sunday check is a fast calendar guard. The message builder is the same
/// <see cref="WeeklySummaryService.Format"/> used by the on-demand endpoint.
/// </summary>
public sealed partial class WeeklySummaryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<WeeklySummaryWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                // Only process on Sundays; cheap guard avoids a DB round-trip on other days.
                if (now.DayOfWeek != DayOfWeek.Sunday)
                {
                    continue;
                }

                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NutriForgeDbContext>();
                var channel = scope.ServiceProvider.GetRequiredService<INotificationChannel>();
                await ProcessDueAsync(db, channel, now, logger, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                LogTickFailed(logger, ex);
            }
#pragma warning restore CA1031
        }
    }

    /// <summary>
    /// Send to every weekly-summary subscriber who hasn't received this Sunday's digest yet.
    /// Testable in isolation.
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

        var today = DateOnly.FromDateTime(now.UtcDateTime); // This Sunday
        var weekStart = today.AddDays(-6);

        var due = await db.ChannelSubscriptions.IgnoreQueryFilters()
            .Where(s => s.Enabled && s.WeeklySummaryEnabled && s.Address != null
                && (s.WeeklyLastSentOn == null || s.WeeklyLastSentOn < today))
            .ToListAsync(ct).ConfigureAwait(false);

        var sent = 0;
        foreach (var sub in due)
        {
            try
            {
                // 7-day totals — IgnoreQueryFilters because we are system context.
                var rows = await db.DiaryEntries.IgnoreQueryFilters()
                    .Where(e => e.UserId == sub.UserId && e.Date >= weekStart && e.Date <= today)
                    .GroupBy(e => e.Date)
                    .Select(g => new { Date = g.Key, Kcal = g.Sum(x => x.Kcal), Protein = g.Sum(x => x.ProteinG) })
                    .ToListAsync(ct).ConfigureAwait(false);

                var targetKcal = await db.Targets.IgnoreQueryFilters()
                    .Where(t => t.UserId == sub.UserId)
                    .Select(t => (double?)t.Kcal)
                    .FirstOrDefaultAsync(ct).ConfigureAwait(false);

                var daysLogged = rows.Count;
                var totalProtein = rows.Sum(r => r.Protein);
                var avgKcal = daysLogged > 0 ? rows.Sum(r => r.Kcal) / daysLogged : 0;

                var message = WeeklySummaryService.Format(today, avgKcal, totalProtein, daysLogged, targetKcal);
                sub.WeeklyLastSentOn = today;

                if (await channel.SendAsync(sub.UserId, message, ct).ConfigureAwait(false))
                {
                    sent++;
                }
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                LogSubscriberFailed(logger, sub.UserId, ex);
            }
#pragma warning restore CA1031
        }

        if (due.Count > 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return sent;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Weekly-summary tick failed; will retry.")]
    private static partial void LogTickFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Weekly summary failed for subscriber {UserId}.")]
    private static partial void LogSubscriberFailed(ILogger logger, Guid userId, Exception ex);
}
