using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Domain.Diary;

namespace NutriForge.Application.Tracking;

public sealed record HydrationDayDto(DateOnly Date, int Ml, int GoalMl);

/// <summary>Add (or subtract, to undo) water for a day. Omit the date for today.</summary>
public sealed record LogHydrationRequest(DateOnly? Date, int Ml);

/// <summary>Set the daily water goal (ml).</summary>
public sealed record SetHydrationGoalRequest(int GoalMl);

/// <summary>
/// Hydration tracking (#72): accumulate a day's water intake and track it against a daily goal. One row
/// per day; logging increments the running total (a negative amount undoes), clamped at zero. The goal is
/// per-user but snapshotted on each day's row, so changing it doesn't rewrite history. Owner-scoped.
/// </summary>
public sealed class HydrationService(IAppDbContext db, IClock clock)
{
    private const int MinGoalMl = 250;
    private const int MaxGoalMl = 10_000;
    private const int MaxDailyMl = 30_000; // a sane upper bound to absorb fat-finger entries

    /// <summary>Add <paramref name="req"/>.Ml to a day's total (negative undoes); clamped to [0, 30 L].</summary>
    public async Task<HydrationDayDto> LogAsync(Guid userId, LogHydrationRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        var date = req.Date ?? clock.Today;

        var day = await db.HydrationDays.FirstOrDefaultAsync(h => h.UserId == userId && h.Date == date, ct).ConfigureAwait(false);
        if (day is null)
        {
            day = new HydrationDay { UserId = userId, Date = date, GoalMl = await CurrentGoalAsync(userId, ct).ConfigureAwait(false) };
            db.HydrationDays.Add(day);
        }

        day.Ml = Math.Clamp(day.Ml + req.Ml, 0, MaxDailyMl);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(day);
    }

    /// <summary>Set the daily goal (clamped to [250 ml, 10 L]); applied to today's row and carried forward.</summary>
    public async Task<HydrationDayDto> SetGoalAsync(Guid userId, SetHydrationGoalRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        var goal = Math.Clamp(req.GoalMl, MinGoalMl, MaxGoalMl);
        var today = clock.Today;

        var day = await db.HydrationDays.FirstOrDefaultAsync(h => h.UserId == userId && h.Date == today, ct).ConfigureAwait(false);
        if (day is null)
        {
            day = new HydrationDay { UserId = userId, Date = today };
            db.HydrationDays.Add(day);
        }

        day.GoalMl = goal;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(day);
    }

    /// <summary>A day's intake and goal. Returns zero ml at the user's current goal when nothing is logged.</summary>
    public async Task<HydrationDayDto> GetDayAsync(Guid userId, DateOnly? date, CancellationToken ct = default)
    {
        var on = date ?? clock.Today;
        var day = await db.HydrationDays.AsNoTracking()
            .FirstOrDefaultAsync(h => h.UserId == userId && h.Date == on, ct).ConfigureAwait(false);
        return day is not null
            ? ToDto(day)
            : new HydrationDayDto(on, 0, await CurrentGoalAsync(userId, ct).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<HydrationDayDto>> HistoryAsync(Guid userId, int days, CancellationToken ct = default)
    {
        var from = clock.Today.AddDays(-(Math.Clamp(days, 1, 730) - 1));
        var rows = await db.HydrationDays.AsNoTracking()
            .Where(h => h.UserId == userId && h.Date >= from)
            .OrderBy(h => h.Date)
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(ToDto).ToList();
    }

    /// <summary>The most recently recorded goal for the user, or the default when they've never set one.</summary>
    private async Task<int> CurrentGoalAsync(Guid userId, CancellationToken ct)
    {
        var goal = await db.HydrationDays.AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.Date)
            .Select(h => (int?)h.GoalMl)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return goal ?? HydrationDay.DefaultGoalMl;
    }

    private static HydrationDayDto ToDto(HydrationDay h) => new(h.Date, h.Ml, h.GoalMl);
}
