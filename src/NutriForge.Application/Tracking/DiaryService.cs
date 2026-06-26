using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Observability;
using NutriForge.Domain.Common;
using NutriForge.Domain.Diary;

namespace NutriForge.Application.Tracking;

/// <summary>Thrown when a diary write references a food that isn't in the catalog.</summary>
public sealed class FoodNotFoundException(Guid foodId)
    : Exception($"Food '{foodId}' was not found in the catalog.")
{
    public Guid FoodId { get; } = foodId;
}

/// <summary>
/// The daily diary. Each entry snapshots its nutrition at log time (ADR-0006) so past days are
/// immutable. Reads roll the day up against the user's cached target.
/// </summary>
public sealed class DiaryService(IAppDbContext db, ICatalogDbContext catalog, TargetService targets, NutriForgeMetrics? metrics = null)
{
    public async Task<DiaryEntryDto> AddAsync(Guid userId, AddDiaryEntryRequest req, CancellationToken ct = default)
    {
        var entry = await BuildEntryAsync(userId, req, ct).ConfigureAwait(false);
        db.DiaryEntries.Add(entry);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        // #50 — day-1 activation + logging-friction telemetry.
        metrics?.DiaryEntryLogged(req.MealSlot.ToString(), req.Method, req.ElapsedSeconds);
        return entry.ToDto();
    }

    /// <summary>
    /// Compute what an entry <em>would</em> look like (resolved food, grams, log-time snapshot)
    /// without saving it — used by the assistant to propose a diary entry for the user to confirm.
    /// </summary>
    public async Task<DiaryEntryDto> PreviewAsync(Guid userId, AddDiaryEntryRequest req, CancellationToken ct = default)
    {
        var entry = await BuildEntryAsync(userId, req, ct).ConfigureAwait(false);
        return entry.ToDto();
    }

    private async Task<DiaryEntry> BuildEntryAsync(Guid userId, AddDiaryEntryRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);

        var food = await catalog.Foods.AsNoTracking()
            .Include(f => f.Portions)
            .FirstOrDefaultAsync(f => f.Id == req.FoodId, ct)
            .ConfigureAwait(false)
            ?? throw new FoodNotFoundException(req.FoodId);

        double grams;
        string portionName;
        if (req.PortionId is { } pid)
        {
            var portion = food.Portions.FirstOrDefault(p => p.Id == pid)
                ?? throw new FoodNotFoundException(req.FoodId);
            grams = portion.Grams * req.Quantity;
            portionName = portion.Name;
        }
        else
        {
            grams = req.Quantity; // logged directly in grams
            portionName = "g";
        }

        var nextSeq = await db.DiaryEntries
            .Where(e => e.UserId == userId && e.Date == req.Date && e.MealSlot == req.MealSlot)
            .Select(e => (int?)e.Sequence).MaxAsync(ct).ConfigureAwait(false) ?? 0;

        var entry = new DiaryEntry
        {
            UserId = userId,
            Date = req.Date,
            MealSlot = req.MealSlot,
            Sequence = nextSeq + 1,
            FoodId = food.Id,
            FoodName = food.Name,
            PortionId = req.PortionId,
            PortionName = portionName,
            Quantity = req.Quantity,
            Grams = grams,
        };
        entry.SetSnapshot(food.MacrosFor(grams).Rounded());
        return entry;
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid entryId, CancellationToken ct = default)
    {
        var entry = await db.DiaryEntries.FirstOrDefaultAsync(e => e.Id == entryId && e.UserId == userId, ct)
            .ConfigureAwait(false);
        if (entry is null)
        {
            return false;
        }

        db.DiaryEntries.Remove(entry);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<DiaryDayDto> GetDayAsync(Guid userId, DateOnly date, CancellationToken ct = default)
    {
        var entries = await db.DiaryEntries.AsNoTracking()
            .Where(e => e.UserId == userId && e.Date == date)
            .OrderBy(e => e.MealSlot).ThenBy(e => e.Sequence)
            .ToListAsync(ct).ConfigureAwait(false);

        var consumed = entries.Aggregate(Macros.Zero, (acc, e) => acc + e.Snapshot);
        var target = await targets.GetAsync(userId, ct).ConfigureAwait(false);

        var remaining = target is null
            ? Macros.Zero
            : new Macros(target.Kcal, target.ProteinG, target.FatG, target.CarbG) + consumed.Scale(-1);

        return new DiaryDayDto(
            date,
            entries.Select(e => e.ToDto()).ToList(),
            MacroTuple.From(consumed),
            target,
            MacroTuple.From(remaining));
    }

    /// <summary>
    /// Distinct recently-logged foods (most recent first) as one-tap re-log candidates, carrying the
    /// portion + quantity last used so re-logging reproduces the same entry.
    /// </summary>
    public async Task<IReadOnlyList<QuickAddFoodDto>> RecentFoodsAsync(Guid userId, int limit, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 50);
        var recent = await db.DiaryEntries.AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt)
            .Take(300) // bound the scan; dedup to distinct (food, portion) below
            .ToListAsync(ct).ConfigureAwait(false);

        return recent
            .GroupBy(e => (e.FoodId, e.PortionId))
            .Select(g => g.First()) // most recent in each group (the list is already newest-first)
            .Take(limit)
            .Select(e => new QuickAddFoodDto(e.FoodId, e.FoodName, e.PortionId, e.PortionName, e.Quantity, e.Kcal, e.ProteinG))
            .ToList();
    }

    /// <summary>
    /// Copy every entry from one day onto another (re-resolving + re-snapshotting current nutrition).
    /// Foods that have since left the catalog are skipped. Returns how many entries were copied.
    /// </summary>
    public async Task<int> CopyDayAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (from == to)
        {
            return 0;
        }

        var source = await db.DiaryEntries.AsNoTracking()
            .Where(e => e.UserId == userId && e.Date == from)
            .OrderBy(e => e.MealSlot).ThenBy(e => e.Sequence)
            .ToListAsync(ct).ConfigureAwait(false);

        var copied = 0;
        foreach (var e in source)
        {
            try
            {
                // AddAsync saves each entry, so the next one's sequence is computed correctly.
                await AddAsync(userId, new AddDiaryEntryRequest(to, e.MealSlot, e.FoodId, e.PortionId, e.Quantity), ct)
                    .ConfigureAwait(false);
                copied++;
            }
            catch (FoodNotFoundException)
            {
                // A food removed from the catalog since — skip it, copy the rest.
            }
        }

        return copied;
    }

    public async Task<IReadOnlyList<TrendDayDto>> GetTrendAsync(
        Guid userId, DateOnly endDate, int days = 7, CancellationToken ct = default)
    {
        var startDate = endDate.AddDays(-(days - 1));

        var byDay = await db.DiaryEntries.AsNoTracking()
            .Where(e => e.UserId == userId && e.Date >= startDate && e.Date <= endDate)
            .GroupBy(e => e.Date)
            .Select(g => new { Date = g.Key, Kcal = g.Sum(x => x.Kcal) })
            .ToListAsync(ct).ConfigureAwait(false);

        var target = await targets.GetAsync(userId, ct).ConfigureAwait(false);
        var targetKcal = target?.Kcal ?? 0;
        var lookup = byDay.ToDictionary(x => x.Date, x => x.Kcal);

        return Enumerable.Range(0, days)
            .Select(offset => startDate.AddDays(offset))
            .Select(d => new TrendDayDto(d, lookup.GetValueOrDefault(d, 0), targetKcal))
            .ToList();
    }
}
