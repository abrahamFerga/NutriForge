using NutriForge.Application.Abstractions;
using NutriForge.Application.Recipes;
using NutriForge.Application.Tracking;

namespace NutriForge.Application.DietGen;

/// <summary>The plan plus how many recipes the agent wrote for it.</summary>
public sealed record AutoDietResult(DietPlanDto Plan, int RecipesGenerated);

/// <summary>
/// The headline "make me a full diet" flow (#101): the agent writes a fresh, varied set of recipes FOR
/// THIS plan and the deterministic planner builds the week from EXACTLY those — it never reads the user's
/// recipe catalog and never pollutes it. The generated recipes are owned by the user (so the plan can
/// reference them) but tagged plan-generated, so they stay out of the Recipes browser. The user authors
/// nothing and never curates a recipe list. Owner-scoped.
/// </summary>
public sealed class AutoDietService(
    DietPlanService plans,
    RecipeGenerationService recipeGen,
    TargetService targets,
    ProfileService profiles)
{
    /// <summary>Distinct recipes to write per meal type (scaled by horizon, then clamped) for weekly variety.</summary>
    private const int MinPerMealType = 2;
    private const int MaxPerMealType = 4;

    /// <summary>Hard cap on recipes written for one plan (cost / latency guard).</summary>
    private const int MaxGenerate = 16;

    public bool IsConfigured => recipeGen.IsConfigured;

    public async Task<AutoDietResult> CreateAsync(Guid userId, CreateDietPlanRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);

        if (!recipeGen.IsConfigured)
        {
            // No AI provider → best-effort over whatever recipes already exist (the only option).
            var fallback = await plans.CreateAsync(userId, req, ct: ct).ConfigureAwait(false);
            return new AutoDietResult(fallback, 0);
        }

        var freshIds = await GenerateFreshAsync(userId, req, ct).ConfigureAwait(false);
        // Build the plan from ONLY the recipes we just wrote — the user's catalog is never read.
        var plan = await plans.CreateAsync(userId, req, freshIds, ct).ConfigureAwait(false);
        return new AutoDietResult(plan, freshIds.Count);
    }

    /// <summary>Write a fresh, varied set of recipes for the plan and return their ids.</summary>
    private async Task<IReadOnlyCollection<Guid>> GenerateFreshAsync(Guid userId, CreateDietPlanRequest req, CancellationToken ct)
    {
        var target = await targets.GetAsync(userId, ct).ConfigureAwait(false);
        var profile = await profiles.GetAsync(userId, ct).ConfigureAwait(false);

        var kcal = req.KcalTarget ?? target?.Kcal ?? 2000;
        var mealsPerDay = Math.Clamp(req.MealsPerDay ?? 3, 1, 6);
        var horizon = Math.Clamp(req.HorizonDays ?? 7, 1, 30);

        var exclude = AllergenOntology
            .Expand((req.ExcludeAllergens ?? []).Concat(profile?.Allergens ?? []))
            .Concat((req.Dislikes ?? []).Concat(profile?.Dislikes ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var mealTypes = MealTypesFor(mealsPerDay);
        // More days ⇒ more distinct recipes per meal so the week doesn't keep repeating (clamped + capped).
        var perType = Math.Clamp((horizon + 1) / 2, MinPerMealType, MaxPerMealType);
        var total = Math.Min(MaxGenerate, perType * mealTypes.Length);
        var perServingKcal = kcal / mealsPerDay;
        IReadOnlyList<string>? forceTags = string.IsNullOrWhiteSpace(req.DietSlug) ? null : [req.DietSlug];

        var ids = new List<Guid>(total);
        foreach (var mealType in mealTypes)
        {
            if (ids.Count >= total)
            {
                break;
            }

            var batch = Math.Min(perType, total - ids.Count);
            var brief = new RecipeBrief(mealType, req.DietSlug, perServingKcal, req.MaxPrepMinutes, exclude, req.Desire, Servings: 4);
            var created = await recipeGen
                .GenerateAsync(userId, brief, batch, forceTags, RecipeGenerationService.PlanGeneratedSource, ct)
                .ConfigureAwait(false);
            ids.AddRange(created.Select(r => r.Id));
        }

        return ids;
    }

    private static string[] MealTypesFor(int mealsPerDay)
    {
        string[] all = ["breakfast", "lunch", "dinner", "snack"];
        return mealsPerDay >= 4 ? all : all[..Math.Max(1, Math.Min(3, mealsPerDay))];
    }
}
