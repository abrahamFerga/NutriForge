using System.Globalization;

namespace NutriForge.Application.Notifications;

/// <summary>The outcome of a weekly-summary send: whether it went out, the channel, and the text.</summary>
public sealed record WeeklySummaryResult(bool Sent, string Channel, string Message);

/// <summary>
/// Builds and sends a user's 7-day progress digest (avg kcal, adherence, total protein) through
/// the configured notification channel. Deterministic message builder shared by the on-demand
/// endpoint and the scheduled Sunday worker.
/// </summary>
public sealed class WeeklySummaryService(Tracking.DiaryService diary, INotificationChannel channel)
{
    public async Task<WeeklySummaryResult> SendForAsync(Guid userId, DateOnly weekEnding, CancellationToken ct = default)
    {
        var weekStart = weekEnding.AddDays(-6);
        double totalKcal = 0, totalProtein = 0;
        int daysLogged = 0;
        double? targetKcal = null;

        for (var d = weekStart; d <= weekEnding; d = d.AddDays(1))
        {
            var day = await diary.GetDayAsync(userId, d, ct).ConfigureAwait(false);
            targetKcal ??= day.Target?.Kcal;
            if (day.Entries.Count > 0)
            {
                daysLogged++;
                totalKcal += day.Consumed.Kcal;
                totalProtein += day.Consumed.ProteinG;
            }
        }

        var avgKcal = daysLogged > 0 ? totalKcal / daysLogged : 0;
        var message = Format(weekEnding, avgKcal, totalProtein, daysLogged, targetKcal);
        var sent = channel.IsConfigured && await channel.SendAsync(userId, message, ct).ConfigureAwait(false);
        return new WeeklySummaryResult(sent, channel.Name, message);
    }

    /// <summary>Deterministic weekly message from primitives.</summary>
    public static string Format(DateOnly weekEnding, double avgKcal, double totalProtein, int daysLogged, double? targetKcal)
    {
        var c = CultureInfo.InvariantCulture;
        var header = string.Format(c, "📊 Week to {0:d MMM}: {1}/{2} days logged · avg {3:0} kcal/day · protein {4:0} g total.",
            weekEnding, daysLogged, 7, avgKcal, totalProtein);

        if (targetKcal is { } t && daysLogged > 0)
        {
            var weeklyTarget = t * 7;
            var weeklyActual = avgKcal * daysLogged;
            var diff = weeklyActual - weeklyTarget;
            var sign = diff >= 0 ? "+" : "";
            return header + string.Format(c, " vs. goal: {0}{1:0} kcal.", sign, diff);
        }

        return header;
    }
}
