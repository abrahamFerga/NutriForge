using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Recipes;
using NutriForge.Application.Tracking;
using NutriForge.Domain.Recipes;

namespace NutriForge.Application.DietGen;

/// <summary>The plan plus how many recipes the agent had to generate to fill the pool.</summary>
public sealed record AutoDietResult(DietPlanDto Plan, int RecipesGenerated);

/// <summary>
/// The headline "make me a full diet" flow (#101): when the catalog doesn't hold enough diet-compatible,
/// nutrition-computed recipes to build a varied plan, the agent generates the shortfall (full recipes with
/// prep steps, resolved deterministically), then the normal deterministic planner runs over the enriched
/// pool. The user authors nothing. Owner-scoped; generated recipes are owned by the caller and reusable.
/// </summary>
public sealed class AutoDietService(
    DietPlanService plans,
    RecipeGenerationService recipeGen,
    ICatalogDbContext catalog,
    TargetService targets,
    ProfileService profiles)
{
    /// <summary>How many qualifying recipes we aim to have per meal before planning (variety headroom).</summary>
    private const int PerMealTarget = 3;

    /// <summary>Hard cap on recipes generated in one auto-diet call (cost / latency guard).</summary>
    private const int MaxGenerate = 12;

    public bool IsConfigured => recipeGen.IsConfigured;

    public async Task<AutoDietResult> CreateAsync(Guid userId, CreateDietPlanRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);

        var generated = 0;
        if (recipeGen.IsConfigured)
        {
            generated = await EnsurePoolAsync(userId, req, ct).ConfigureAwait(false);
        }

        // Run the same deterministic pipeline the normal flow uses — it re-queries the (now enriched)
        // catalog, so the freshly generated recipes are included, and the saved household still attaches.
        var plan = await plans.CreateAsync(userId, req, ct).ConfigureAwait(false);
        return new AutoDietResult(plan, generated);
    }

    /// <summary>Generate recipes (distributed across meal types) until the diet-compatible pool is deep enough.</summary>
    private async Task<int> EnsurePoolAsync(Guid userId, CreateDietPlanRequest req, CancellationToken ct)
    {
        var target = await targets.GetAsync(userId, ct).ConfigureAwait(false);
        var profile = await profiles.GetAsync(userId, ct).ConfigureAwait(false);

        var kcal = req.KcalTarget ?? target?.Kcal ?? 2000;
        var mealsPerDay = Math.Clamp(req.MealsPerDay ?? 3, 1, 6);

        var exclude = AllergenOntology
            .Expand((req.ExcludeAllergens ?? []).Concat(profile?.Allergens ?? []))
            .Concat((req.Dislikes ?? []).Concat(profile?.Dislikes ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // How many qualifying recipes do we already have?
        var recipes = await catalog.Recipes.AsNoTracking().Include(r => r.Ingredients)
            .Where(r => r.IsNutritionComputed).ToListAsync(ct).ConfigureAwait(false);
        var diet = string.IsNullOrWhiteSpace(req.DietSlug)
            ? null
            : await catalog.DietTypes.AsNoTracking().FirstOrDefaultAsync(d => d.Slug == req.DietSlug, ct).ConfigureAwait(false);

        var intent = new DietIntent(kcal, null, req.DietSlug, [.. exclude], req.MaxPrepMinutes, mealsPerDay, 1);
        var have = RecipeFilter.Filter(recipes, intent, diet).Count;

        var need = Math.Min(MaxGenerate, Math.Max(0, (mealsPerDay * PerMealTarget) - have));
        if (need == 0)
        {
            return 0;
        }

        var mealTypes = MealTypesFor(mealsPerDay);
        var perType = (int)Math.Ceiling(need / (double)mealTypes.Length);
        var perServingKcal = kcal / mealsPerDay;
        IReadOnlyList<string>? forceTags = string.IsNullOrWhiteSpace(req.DietSlug) ? null : [req.DietSlug];

        var generated = 0;
        foreach (var mealType in mealTypes)
        {
            if (generated >= need)
            {
                break;
            }

            var batch = Math.Min(perType, need - generated);
            var brief = new RecipeBrief(mealType, req.DietSlug, perServingKcal, req.MaxPrepMinutes, exclude, req.Desire, Servings: 4);
            var created = await recipeGen.GenerateAsync(userId, brief, batch, forceTags, ct).ConfigureAwait(false);
            generated += created.Count;
        }

        return generated;
    }

    private static string[] MealTypesFor(int mealsPerDay)
    {
        string[] all = ["breakfast", "lunch", "dinner", "snack"];
        return mealsPerDay >= 4 ? all : all[..Math.Max(1, Math.Min(3, mealsPerDay))];
    }
}
