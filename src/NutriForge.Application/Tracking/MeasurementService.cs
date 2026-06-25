using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Domain.Diary;

namespace NutriForge.Application.Tracking;

public sealed record MeasurementDto(DateOnly Date, double WeightKg, double? BodyFatPct, double? WaistCm);

public sealed record LogMeasurementRequest(DateOnly? Date, double WeightKg, double? BodyFatPct, double? WaistCm);

/// <summary>
/// Body-measurement tracking (#71): log weight (+ optional body-fat % / waist) per day and read the
/// recent history for a trend. One entry per day — logging again the same day updates it. Owner-scoped.
/// </summary>
public sealed class MeasurementService(IAppDbContext db, IClock clock)
{
    public async Task<MeasurementDto> LogAsync(Guid userId, LogMeasurementRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        var date = req.Date ?? clock.Today;

        var entry = await db.BodyMeasurements
            .FirstOrDefaultAsync(m => m.UserId == userId && m.Date == date, ct).ConfigureAwait(false);
        if (entry is null)
        {
            entry = new BodyMeasurement { UserId = userId, Date = date };
            db.BodyMeasurements.Add(entry);
        }

        entry.WeightKg = req.WeightKg;
        entry.BodyFatPct = req.BodyFatPct;
        entry.WaistCm = req.WaistCm;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entry);
    }

    public async Task<IReadOnlyList<MeasurementDto>> HistoryAsync(Guid userId, int days, CancellationToken ct = default)
    {
        var from = clock.Today.AddDays(-(Math.Clamp(days, 1, 730) - 1));
        var rows = await db.BodyMeasurements.AsNoTracking()
            .Where(m => m.UserId == userId && m.Date >= from)
            .OrderBy(m => m.Date)
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(ToDto).ToList();
    }

    public async Task<bool> DeleteAsync(Guid userId, DateOnly date, CancellationToken ct = default)
    {
        var row = await db.BodyMeasurements
            .FirstOrDefaultAsync(m => m.UserId == userId && m.Date == date, ct).ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }

        db.BodyMeasurements.Remove(row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    private static MeasurementDto ToDto(BodyMeasurement m) => new(m.Date, m.WeightKg, m.BodyFatPct, m.WaistCm);
}
