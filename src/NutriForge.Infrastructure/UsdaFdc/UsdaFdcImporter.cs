using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Domain.Catalog;
using NutriForge.Domain.Common;

namespace NutriForge.Infrastructure.UsdaFdc;

/// <summary>The outcome of a USDA import batch.</summary>
public sealed record UsdaImportResult(int Inserted, int Updated)
{
    public int Total => Inserted + Updated;
}

/// <summary>
/// Upserts USDA FoodData Central foods into the catalog, keyed idempotently by
/// <see cref="FoodSource"/> = (<c>usda</c>, FDC id). Re-running the same batch updates the existing
/// rows in place — never a duplicate (the unique (provider, providerId) index backs this). Nutrition is
/// taken verbatim from the authoritative source (USDA per-100 g), so these foods are tier
/// <see cref="VerificationStatus.Authoritative"/>. Never on a request path — run by the import worker.
/// </summary>
public sealed class UsdaFdcImporter(ICatalogDbContext db)
{
    public const string Provider = "usda";

    public async Task<UsdaImportResult> ImportAsync(IReadOnlyList<UsdaFood> foods, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(foods);
        if (foods.Count == 0)
        {
            return new UsdaImportResult(0, 0);
        }

        var ids = foods.Select(f => f.FdcId).ToHashSet();
        var existing = await db.Foods
            .Where(f => f.Source.Provider == Provider && ids.Contains(f.Source.ProviderId))
            .ToDictionaryAsync(f => f.Source.ProviderId, ct).ConfigureAwait(false);

        var inserted = 0;
        var updated = 0;
        foreach (var u in foods)
        {
            var profile = new NutrientProfile
            {
                KcalPer100g = u.KcalPer100g,
                ProteinPer100g = u.ProteinPer100g,
                FatPer100g = u.FatPer100g,
                CarbPer100g = u.CarbPer100g,
            };

            if (existing.TryGetValue(u.FdcId, out var food))
            {
                // Refresh authoritative values in place — the row's identity (and portions) is stable.
                food.Name = u.Description;
                food.NutrientProfile = profile;
                updated++;
            }
            else
            {
                var created = new Food
                {
                    Name = u.Description,
                    Source = new FoodSource(Provider, u.FdcId),
                    VerificationStatus = VerificationStatus.Authoritative,
                    NutrientProfile = profile,
                };
                foreach (var p in u.Portions)
                {
                    created.AddPortion(p.Name, p.Grams);
                }

                db.Foods.Add(created);
                inserted++;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new UsdaImportResult(inserted, updated);
    }
}
