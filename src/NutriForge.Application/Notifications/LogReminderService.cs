namespace NutriForge.Application.Notifications;

/// <summary>The outcome of a log-reminder send.</summary>
public sealed record LogReminderResult(bool Sent, string Channel, string Message);

/// <summary>
/// Sends a "time to log" nudge when the user hasn't recorded any diary entries for today.
/// The reminder is opt-in (set via <c>ReminderHourUtc</c> on the subscription) and fires at
/// most once per day regardless of how many times the worker runs.
/// </summary>
public sealed class LogReminderService(Tracking.DiaryService diary, INotificationChannel channel)
{
    private const string ReminderMessage =
        "🔔 Time to track your meals! Open NutriForge and log what you've eaten today.";

    /// <returns>Null if the user has already logged today and no reminder is needed.</returns>
    public async Task<LogReminderResult?> MaybeSendAsync(Guid userId, DateOnly date, CancellationToken ct = default)
    {
        var day = await diary.GetDayAsync(userId, date, ct).ConfigureAwait(false);
        if (day.Entries.Count > 0)
        {
            return null; // already logged — no nudge needed
        }

        var sent = channel.IsConfigured && await channel.SendAsync(userId, ReminderMessage, ct).ConfigureAwait(false);
        return new LogReminderResult(sent, channel.Name, ReminderMessage);
    }
}
