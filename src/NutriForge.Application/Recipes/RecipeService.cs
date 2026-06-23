using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Domain.Common;
using NutriForge.Domain.Recipes;

namespace NutriForge.Application.Recipes;

/// <summary>
/// Recipe catalog: create recipes whose per-serving nutrition is <b>computed</b> from resolved
/// ingredients (the differentiator — never trusted from the source), read, list, and scale.
/// Ingredient resolution and unit→grams conversion are fully deterministic.
/// </summary>
public sealed class RecipeService(ICatalogDbContext db)
{
    public async Task<RecipeDto> CreateAsync(CreateRecipeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ingredients = await db.Ingredients.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var foodIds = ingredients.Where(i => i.DefaultFoodId.HasValue).Select(i => i.DefaultFoodId!.Value).ToHashSet();
        var foods = await db.Foods.AsNoTracking()
            .Where(f => foodIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, ct).ConfigureAwait(false);

        var recipe = new Recipe
        {
            Name = request.Name.Trim(),
            Servings = request.Servings <= 0 ? 1 : request.Servings,
            TotalMinutes = request.TotalMinutes,
            Instructions = request.Instructions,
            Tags = [.. (request.Tags ?? []).Select(t => t.Trim().ToLowerInvariant())],
        };

        foreach (var line in request.Ingredients ?? [])
        {
            var ri = new RecipeIngredient
            {
                RawText = line.RawText ?? $"{line.Quantity} {line.Unit} {line.Name}".Trim(),
                Quantity = line.Quantity,
                Unit = line.Unit,
                IngredientName = line.Name.Trim(),
            };

            var ingredient = ingredients.FirstOrDefault(i => i.Matches(line.Name));
            if (ingredient is not null)
            {
                ri.IngredientId = ingredient.Id;
                var grams = UnitConverter.ToGrams(line.Quantity, line.Unit, ingredient.DensityGPerMl, ingredient.GramsPerCount);
                if (grams is { } g && ingredient.DefaultFoodId is { } foodId && foods.TryGetValue(foodId, out var food))
                {
                    ri.SetContribution(g, food.MacrosFor(g).Rounded());
                }
            }

            recipe.AddIngredient(ri);
        }

        recipe.RecomputeNutrition();
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return recipe.ToDto();
    }

    public async Task<RecipeDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var recipe = await db.Recipes.AsNoTracking()
            .Include(r => r.Ingredients)
            .FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);
        return recipe?.ToDto();
    }

    public async Task<IReadOnlyList<RecipeSummary>> ListAsync(string? query, CancellationToken ct = default)
    {
        var q = db.Recipes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim().ToLowerInvariant();
            q = q.Where(r => r.Name.ToLower().Contains(term));
        }

        var recipes = await q.OrderBy(r => r.Name).Take(100).ToListAsync(ct).ConfigureAwait(false);
        return recipes.Select(r => r.ToSummary()).ToList();
    }

    public async Task<ScaledRecipeDto?> ScaleAsync(Guid id, double targetServings, CancellationToken ct = default)
    {
        var recipe = await db.Recipes.AsNoTracking()
            .Include(r => r.Ingredients)
            .FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);
        if (recipe is null)
        {
            return null;
        }

        var factor = targetServings / (recipe.Servings <= 0 ? 1 : recipe.Servings);
        var scaled = recipe.Ingredients
            .Select(i => new ScaledIngredientDto(i.IngredientName, Math.Round(i.Quantity * factor, 2), i.Unit, Math.Round(i.Grams * factor, 1)))
            .ToList();

        // Per-serving nutrition is invariant under scaling.
        return new ScaledRecipeDto(recipe.Id, recipe.Name, recipe.Servings, targetServings, scaled, recipe.KcalPerServing);
    }
}
