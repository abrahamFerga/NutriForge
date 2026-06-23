using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
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
public sealed class DiaryService(IAppDbContext db, ICatalogDbContext catalog, TargetService targets)
{
    public async Task<DiaryEntryDto> AddAsync(Guid userId, AddDiaryEntryRequest req, CancellationToken ct = default)
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

        db.DiaryEntries.Add(entry);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return entry.ToDto();
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
