using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Domain.Planning;

namespace NutriForge.Application.Planning;

public sealed record ShoppingRecipeLine(Guid RecipeId, double Servings);
public sealed record GenerateShoppingListRequest(IReadOnlyList<ShoppingRecipeLine> Recipes);

public sealed record ShoppingItemDto(string IngredientName, double Grams, bool PantryCovered);
public sealed record ShoppingAisleDto(string Aisle, IReadOnlyList<ShoppingItemDto> Items);
public sealed record ShoppingListDto(Guid Id, Guid? MealPlanId, IReadOnlyList<ShoppingAisleDto> Aisles);

/// <summary>
/// Builds the consolidated, aisle-grouped, pantry-aware shopping list: expand recipes → sum by
/// canonical ingredient → subtract pantry stock → group by aisle. The part most meal-prep apps
/// get wrong, and the closed loop's endpoint from an accepted plan.
/// </summary>
public sealed class ShoppingListService(IAppDbContext db, ICatalogDbContext catalog)
{
    private sealed class Line
    {
        public Guid? IngredientId;
        public string Name = string.Empty;
        public string Aisle = "Other";
        public double Grams;
    }

    public async Task<ShoppingListDto> GenerateAsync(
        Guid userId, IReadOnlyList<ShoppingRecipeLine> lines, Guid? mealPlanId, CancellationToken ct = default)
    {
        var list = await BuildAsync(userId, lines, mealPlanId, ct).ConfigureAwait(false);
        db.ShoppingLists.Add(list);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(list);
    }

    /// <summary>Consolidate the list WITHOUT persisting it — e.g. to render a PDF or preview.</summary>
    public async Task<ShoppingListDto> ComputeAsync(
        Guid userId, IReadOnlyList<ShoppingRecipeLine> lines, Guid? mealPlanId = null, CancellationToken ct = default)
    {
        var list = await BuildAsync(userId, lines, mealPlanId, ct).ConfigureAwait(false);
        return ToDto(list);
    }

    private async Task<ShoppingList> BuildAsync(
        Guid userId, IReadOnlyList<ShoppingRecipeLine> lines, Guid? mealPlanId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var recipeIds = lines.Select(l => l.RecipeId).ToHashSet();
        var recipes = await catalog.Recipes.AsNoTracking()
            .Include(r => r.Ingredients)
            .Where(r => recipeIds.Contains(r.Id))
            .ToListAsync(ct).ConfigureAwait(false);
        var ingredients = await catalog.Ingredients.AsNoTracking()
            .ToDictionaryAsync(i => i.Id, ct).ConfigureAwait(false);

        // Consolidate grams by canonical ingredient (fall back to name when unresolved).
        var consolidated = new Dictionary<string, Line>(StringComparer.OrdinalIgnoreCase);
        foreach (var reqLine in lines)
        {
            var recipe = recipes.FirstOrDefault(r => r.Id == reqLine.RecipeId);
            if (recipe is null)
            {
                continue;
            }

            var factor = reqLine.Servings / (recipe.Servings <= 0 ? 1 : recipe.Servings);
            foreach (var ri in recipe.Ingredients)
            {
                var key = ri.IngredientId?.ToString() ?? ri.IngredientName.ToLowerInvariant();
                if (!consolidated.TryGetValue(key, out var line))
                {
                    var aisle = ri.IngredientId is { } id && ingredients.TryGetValue(id, out var ing) ? ing.AisleCategory : "Other";
                    line = new Line { IngredientId = ri.IngredientId, Name = ri.IngredientName, Aisle = aisle };
                    consolidated[key] = line;
                }

                line.Grams += ri.Grams * factor;
            }
        }

        // Subtract pantry stock.
        var pantry = await db.PantryItems.AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToDictionaryAsync(p => p.IngredientId, p => p.Grams, ct).ConfigureAwait(false);

        var list = new ShoppingList { UserId = userId, MealPlanId = mealPlanId };
        foreach (var line in consolidated.Values)
        {
            var onHand = line.IngredientId is { } id && pantry.TryGetValue(id, out var g) ? g : 0;
            var needed = line.Grams - onHand;
            list.AddItem(new ShoppingItem
            {
                IngredientId = line.IngredientId,
                IngredientName = line.Name,
                AisleCategory = line.Aisle,
                Grams = Math.Max(needed, 0),
                PantryCovered = needed <= 0,
            });
        }

        return list;
    }

    public async Task<ShoppingListDto?> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var list = await db.ShoppingLists.AsNoTracking()
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct).ConfigureAwait(false);
        return list is null ? null : ToDto(list);
    }

    private static ShoppingListDto ToDto(ShoppingList list)
    {
        var aisles = list.Items
            .GroupBy(i => i.AisleCategory)
            .OrderBy(g => g.Key)
            .Select(g => new ShoppingAisleDto(
                g.Key,
                g.OrderBy(i => i.IngredientName)
                    .Select(i => new ShoppingItemDto(i.IngredientName, Math.Round(i.Grams, 1), i.PantryCovered)).ToList()))
            .ToList();
        return new ShoppingListDto(list.Id, list.MealPlanId, aisles);
    }
}
