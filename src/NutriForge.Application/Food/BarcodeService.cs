using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;

namespace NutriForge.Application.Food;

/// <summary>
/// Barcode logging: look up a GTIN in the local catalog and, on a miss, fetch it from Open Food
/// Facts and upsert it so the next scan is instant (fetch-on-miss). The fetched food carries its
/// ODbL provenance on <c>Source</c>.
/// </summary>
public sealed class BarcodeService(ICatalogDbContext db, IOpenFoodFactsClient openFoodFacts)
{
    public async Task<FoodDetail?> LookupAsync(string gtin, CancellationToken ct = default)
    {
        var code = (gtin ?? string.Empty).Trim();
        if (code.Length == 0)
        {
            return null;
        }

        var existing = await db.Foods
            .AsNoTracking()
            .Include(f => f.Portions)
            .OrderBy(f => f.VerificationStatus)
            .FirstOrDefaultAsync(f => f.Gtin == code, ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing.ToDetail();
        }

        var fetched = await openFoodFacts.FetchByBarcodeAsync(code, ct).ConfigureAwait(false);
        if (fetched is null)
        {
            return null;
        }

        db.Foods.Add(fetched);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return fetched.ToDetail();
    }
}
