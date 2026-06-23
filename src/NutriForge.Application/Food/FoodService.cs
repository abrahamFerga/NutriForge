using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Domain.Catalog;
using NutriForge.Domain.Common;

namespace NutriForge.Application.Food;

/// <summary>
/// Trustworthy food search and catalog reads. Results are ranked by verification tier and the
/// tier is always carried through — tiers are never silently blended (SPEC). Search is cached.
/// </summary>
public sealed class FoodService(ICatalogDbContext db, IFoodSearchCache cache)
{
    private const int MaxResults = 25;

    public async Task<IReadOnlyList<FoodSummary>> SearchAsync(string query, CancellationToken ct = default)
    {
        var q = (query ?? string.Empty).Trim().ToLowerInvariant();
        if (q.Length < 2)
        {
            return [];
        }

        var cached = await cache.GetAsync(q, ct).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var results = await db.Foods
            .AsNoTracking()
            .Include(f => f.Portions)
            .Where(f => f.Name.ToLower().Contains(q)
                || (f.Brand != null && f.Brand.ToLower().Contains(q))
                || (f.Gtin != null && f.Gtin == q))
            .OrderBy(f => f.VerificationStatus) // Curated(1) → … → UserSubmitted(4)
            .ThenBy(f => f.Name)
            .Take(MaxResults)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var summaries = results.Select(f => f.ToSummary()).ToList();
        await cache.SetAsync(q, summaries, ct).ConfigureAwait(false);
        return summaries;
    }

    public async Task<FoodDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var food = await db.Foods
            .AsNoTracking()
            .Include(f => f.Portions)
            .FirstOrDefaultAsync(f => f.Id == id, ct)
            .ConfigureAwait(false);

        return food?.ToDetail();
    }

    public async Task<FoodDetail?> GetByBarcodeAsync(string gtin, CancellationToken ct = default)
    {
        var food = await db.Foods
            .AsNoTracking()
            .Include(f => f.Portions)
            .OrderBy(f => f.VerificationStatus)
            .FirstOrDefaultAsync(f => f.Gtin == gtin, ct)
            .ConfigureAwait(false);

        return food?.ToDetail();
    }

    public async Task<FoodDetail> SubmitAsync(SubmitFoodRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var food = new Domain.Catalog.Food
        {
            Name = request.Name.Trim(),
            Brand = string.IsNullOrWhiteSpace(request.Brand) ? null : request.Brand.Trim(),
            Gtin = string.IsNullOrWhiteSpace(request.Gtin) ? null : request.Gtin.Trim(),
            VerificationStatus = VerificationStatus.UserSubmitted,
            Source = new FoodSource(FoodSource.UserSubmittedProvider, Guid.CreateVersion7().ToString()),
            NutrientProfile = new NutrientProfile
            {
                KcalPer100g = request.KcalPer100g,
                ProteinPer100g = request.ProteinPer100g,
                FatPer100g = request.FatPer100g,
                CarbPer100g = request.CarbPer100g,
            },
        };

        foreach (var p in request.Portions ?? [])
        {
            food.AddPortion(p.Name, p.Grams);
        }

        db.Foods.Add(food);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return food.ToDetail();
    }
}
