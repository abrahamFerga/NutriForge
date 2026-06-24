using System.Globalization;
using NutriForge.Application.Tracking;

namespace NutriForge.Application.Notifications;

/// <summary>The outcome of a daily-summary send: whether it went out, the channel, and the text.</summary>
public sealed record DailySummaryResult(bool Sent, string Channel, string Message);

/// <summary>
/// Builds and sends a user's daily calorie summary (consumed vs. target) through the configured
/// notification channel. The message is derived deterministically from the diary day — the same
/// path the on-demand endpoint and (later) the scheduled worker call.
/// </summary>
public sealed class DailySummaryService(DiaryService diary, INotificationChannel channel)
{
    public async Task<DailySummaryResult> SendForAsync(Guid userId, DateOnly date, CancellationToken ct = default)
    {
        var day = await diary.GetDayAsync(userId, date, ct).ConfigureAwait(false);
        var message = BuildMessage(day);
        var sent = channel.IsConfigured && await channel.SendAsync(userId, message, ct).ConfigureAwait(false);
        return new DailySummaryResult(sent, channel.Name, message);
    }

    /// <summary>The user-facing summary line — deterministic, derived only from the day's totals.</summary>
    internal static string BuildMessage(DiaryDayDto day)
    {
        var c = CultureInfo.InvariantCulture;
        var kcal = day.Consumed.Kcal;
        var protein = day.Consumed.ProteinG;

        if (day.Target is { } t)
        {
            var remaining = t.Kcal - kcal;
            var verb = remaining >= 0
                ? $"{remaining.ToString("0", c)} kcal to go"
                : $"{(-remaining).ToString("0", c)} kcal over";
            return string.Format(c, "🍽 {0:ddd d MMM}: {1:0} / {2:0} kcal · {3} · protein {4:0} g.",
                day.Date, kcal, t.Kcal, verb, protein);
        }

        return string.Format(c, "🍽 {0:ddd d MMM}: {1:0} kcal logged, protein {2:0} g. Set your profile to get targets.",
            day.Date, kcal, protein);
    }
}
