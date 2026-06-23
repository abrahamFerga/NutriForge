using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Domain.Planning;
using NutriForge.Domain.Recipes;

namespace NutriForge.Application.Planning;

public sealed record AddPantryRequest(string IngredientName, double Quantity, string? Unit);
public sealed record PantryItemDto(Guid Id, string IngredientName, double Grams);

/// <summary>On-hand pantry stock (owner-scoped), subtracted when a shopping list is generated.</summary>
public sealed class PantryService(IAppDbContext db, ICatalogDbContext catalog)
{
    public async Task<PantryItemDto> AddAsync(Guid userId, AddPantryRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);

        var ingredients = await catalog.Ingredients.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var ingredient = ingredients.FirstOrDefault(i => i.Matches(req.IngredientName));
        var grams = UnitConverter.ToGrams(req.Quantity, req.Unit, ingredient?.DensityGPerMl, ingredient?.GramsPerCount)
            ?? req.Quantity;

        var ingredientId = ingredient?.Id ?? Guid.Empty;
        var existing = ingredientId != Guid.Empty
            ? await db.PantryItems.FirstOrDefaultAsync(p => p.UserId == userId && p.IngredientId == ingredientId, ct).ConfigureAwait(false)
            : null;

        if (existing is null)
        {
            existing = new PantryItem
            {
                UserId = userId,
                IngredientId = ingredientId,
                IngredientName = ingredient?.CanonicalName ?? req.IngredientName.Trim(),
            };
            db.PantryItems.Add(existing);
        }

        existing.Grams = grams;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new PantryItemDto(existing.Id, existing.IngredientName, existing.Grams);
    }

    public async Task<IReadOnlyList<PantryItemDto>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var items = await db.PantryItems.AsNoTracking()
            .Where(p => p.UserId == userId).OrderBy(p => p.IngredientName)
            .ToListAsync(ct).ConfigureAwait(false);
        return items.Select(p => new PantryItemDto(p.Id, p.IngredientName, p.Grams)).ToList();
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var item = await db.PantryItems.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return false;
        }

        db.PantryItems.Remove(item);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }
}
