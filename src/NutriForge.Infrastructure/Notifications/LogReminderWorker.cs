using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NutriForge.Application.Notifications;
using NutriForge.Infrastructure.Persistence;

namespace NutriForge.Infrastructure.Notifications;

/// <summary>
/// Sends a "time to log your meals" nudge to users who haven't recorded any diary entries today
/// when their configured <c>ReminderHourUtc</c> arrives. Fires at most once a day per user.
/// The real diary check is done against their data; the system context bypasses the per-user filter.
/// </summary>
public sealed partial class LogReminderWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<LogReminderWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    private const string ReminderMessage =
        "🔔 Time to track your meals! Open NutriForge and log what you've eaten today.";

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
#pragma warning disable CA1031
            catch (Exception ex)
            {
                LogTickFailed(logger, ex);
            }
#pragma warning restore CA1031
        }
    }

    /// <summary>
    /// Send to every reminder-enabled subscriber due at this UTC hour who hasn't logged today.
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

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var hour = now.UtcDateTime.Hour;

        var due = await db.ChannelSubscriptions.IgnoreQueryFilters()
            .Where(s => s.Enabled && s.ReminderHourUtc == hour && s.Address != null
                && (s.ReminderLastSentOn == null || s.ReminderLastSentOn < today))
            .ToListAsync(ct).ConfigureAwait(false);

        var sent = 0;
        foreach (var sub in due)
        {
            try
            {
                // Check if the user has already logged something today.
                var hasLogged = await db.DiaryEntries.IgnoreQueryFilters()
                    .AnyAsync(e => e.UserId == sub.UserId && e.Date == today, ct).ConfigureAwait(false);

                if (hasLogged)
                {
                    continue; // already tracked today — skip the nudge
                }

                sub.ReminderLastSentOn = today;
                if (await channel.SendAsync(sub.UserId, ReminderMessage, ct).ConfigureAwait(false))
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Log-reminder tick failed; will retry.")]
    private static partial void LogTickFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Log reminder failed for subscriber {UserId}.")]
    private static partial void LogSubscriberFailed(ILogger logger, Guid userId, Exception ex);
}
