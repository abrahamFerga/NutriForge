using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Domain.Diary;

namespace NutriForge.Application.Tracking;

/// <summary>
/// Starred foods for one-tap logging (#69). The favorite is a thin link; food details (name, portion,
/// macros) are read live from the catalog, so a relabel/renutrition is reflected. Owner-scoped.
/// </summary>
public sealed class FavoriteService(IAppDbContext db, ICatalogDbContext catalog)
{
    /// <summary>Star a food. Returns false when the food isn't in the catalog; idempotent otherwise.</summary>
    public async Task<bool> AddAsync(Guid userId, Guid foodId, CancellationToken ct = default)
    {
        var known = await catalog.Foods.AsNoTracking().AnyAsync(f => f.Id == foodId, ct).ConfigureAwait(false);
        if (!known)
        {
            return false;
        }

        var already = await db.FavoriteFoods.AnyAsync(f => f.UserId == userId && f.FoodId == foodId, ct).ConfigureAwait(false);
        if (!already)
        {
            db.FavoriteFoods.Add(new FavoriteFood { UserId = userId, FoodId = foodId });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return true;
    }

    public async Task<bool> RemoveAsync(Guid userId, Guid foodId, CancellationToken ct = default)
    {
        var fav = await db.FavoriteFoods.FirstOrDefaultAsync(f => f.UserId == userId && f.FoodId == foodId, ct).ConfigureAwait(false);
        if (fav is null)
        {
            return false;
        }

        db.FavoriteFoods.Remove(fav);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>The user's favorites as one-tap re-log candidates (default to the food's first portion).</summary>
    public async Task<IReadOnlyList<QuickAddFoodDto>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var favIds = await db.FavoriteFoods.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => f.FoodId)
            .ToListAsync(ct).ConfigureAwait(false);
        if (favIds.Count == 0)
        {
            return [];
        }

        var foods = await catalog.Foods.AsNoTracking().Include(f => f.Portions)
            .Where(f => favIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, ct).ConfigureAwait(false);

        var result = new List<QuickAddFoodDto>();
        foreach (var id in favIds) // preserve the favorited order
        {
            if (!foods.TryGetValue(id, out var food))
            {
                continue; // a starred food later removed from the catalog
            }

            var portion = food.Portions.FirstOrDefault();
            var grams = portion?.Grams ?? 100;
            var macros = food.MacrosFor(grams).Rounded();
            result.Add(new QuickAddFoodDto(
                food.Id, food.Name, portion?.Id, portion?.Name ?? "g",
                portion is null ? 100 : 1, macros.Kcal, macros.ProteinG));
        }

        return result;
    }
}
