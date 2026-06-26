using NutriForge.Domain.Common;
using NutriForge.Domain.Recipes;

namespace NutriForge.Application.DietGen;

/// <summary>One selected meal: a recipe at a day + slot with (adjustable) servings.</summary>
public sealed class PlannedSlot
{
    public int Day { get; set; }
    public MealSlot Meal { get; set; }
    public Recipe Recipe { get; init; } = null!;
    public double Servings { get; set; } = 1;

    public Macros Macros => Recipe.MacrosFor(Servings);
}

public sealed record GenerationResult(bool Feasible, IReadOnlyList<PlannedSlot> Slots, Macros DailyAverage, string Message);

/// <summary>FILTER (step 2) — pure, deterministic hard constraints. No LLM; allergens are enforced here.</summary>
public static class RecipeFilter
{
    public static IReadOnlyList<Recipe> Filter(IEnumerable<Recipe> recipes, DietIntent intent, DietType? diet)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return recipes.Where(r =>
            r.IsNutritionComputed
            && PassesDiet(r, diet)
            && (intent.MaxPrepMinutes is null || r.TotalMinutes <= intent.MaxPrepMinutes)
            && !intent.ExcludeKeywords.Any(k => Contains(r, k)))
            .ToList();
    }

    private static bool PassesDiet(Recipe r, DietType? diet)
    {
        if (diet is null)
        {
            return true;
        }

        if (!diet.RequiredTags.All(t => r.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !diet.ExcludedKeywords.Any(k => Contains(r, k));
    }

    /// <summary>True if a recipe's name or any ingredient contains the keyword (the allergen/exclusion gate).</summary>
    public static bool Contains(Recipe r, string keyword)
    {
        var k = keyword.Trim().ToLowerInvariant();
        if (k.Length == 0)
        {
            return false;
        }

        return r.Name.Contains(k, StringComparison.OrdinalIgnoreCase)
            || r.Ingredients.Any(ri => ri.IngredientName.Contains(k, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>VERIFY (step 4) — the trust anchor. Computes the plan's real nutrition vs. the target.</summary>
public static class PlanVerifier
{
    public const double KcalTolerancePct = 0.05;

    // distinctMealDays = the number of distinct meal-sets generated (= NumBlocks). Every calendar day eats
    // exactly one of these sets, so the per-DAY average is the total over the distinct sets ÷ that count.
    public static (bool Ok, Macros DailyAverage) Verify(IReadOnlyList<PlannedSlot> slots, double kcalTarget, int distinctMealDays)
    {
        var days = Math.Max(distinctMealDays, 1);
        var total = slots.Aggregate(Macros.Zero, (acc, s) => acc + s.Macros);
        var avg = total.Scale(1.0 / days).Rounded();
        var ok = kcalTarget > 0 && Math.Abs(avg.Kcal - kcalTarget) <= kcalTarget * KcalTolerancePct;
        return (ok, avg);
    }

    /// <summary>Re-assert that no slot's recipe contains an excluded keyword (defense in depth).</summary>
    public static bool AllergensClear(IReadOnlyList<PlannedSlot> slots, IReadOnlyList<string> excludeKeywords) =>
        !slots.Any(s => excludeKeywords.Any(k => RecipeFilter.Contains(s.Recipe, k)));
}

/// <summary>A slot's per-serving macros + day, handed to the LP optimizer (REPAIR step 5a).</summary>
public sealed record OptimizerSlot(int Day, double KcalPerServing, double ProteinPerServing);

/// <summary>REPAIR — portion tuning via a linear program (OR-Tools). Holds selection fixed.</summary>
public interface IPortionOptimizer
{
    /// <summary>New servings per slot (same order) that minimize deviation from the daily targets.</summary>
    IReadOnlyList<double> Optimize(
        IReadOnlyList<OptimizerSlot> slots, double dailyKcalTarget, double? dailyProteinTarget,
        double minServings, double maxServings);
}

/// <summary>
/// The hybrid generate-and-check generator: SELECT → VERIFY → LP REPAIR → VERIFY. SELECT is greedy
/// and deterministic by default; an optional MAF agent (#36) may instead compose the selection to
/// optimize soft objectives (variety, no-repeat, cuisine balance, batching). Either way the LLM never
/// owns a number and never bypasses a check — its picks pass through the identical VERIFY/REPAIR/
/// allergen path, and any missing/garbage/unsafe proposal falls back to greedy.
/// </summary>
public sealed class DietPlanGenerator(IPortionOptimizer optimizer, IMealSelectAgent? selectAgent = null)
{
    private static readonly MealSlot[] AllMeals = [MealSlot.Breakfast, MealSlot.Lunch, MealSlot.Dinner, MealSlot.Snack];

    /// <summary>Fully-deterministic generation (greedy SELECT). No LLM is ever consulted.</summary>
    public GenerationResult Generate(IReadOnlyList<Recipe> pool, DietIntent intent, double kcalTarget)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(intent);

        if (Layout(pool, intent, kcalTarget) is not { } layout)
        {
            return EmptyPoolResult();
        }

        var slots = GreedySelect(pool, layout);
        return Finalize(slots, intent, kcalTarget, layout.NumBlocks);
    }

    /// <summary>
    /// Generation that lets the meal-select agent (when configured) compose SELECT, then runs the same
    /// deterministic VERIFY → REPAIR → allergen re-check. Falls back to <see cref="Generate"/>'s greedy
    /// selection when no agent is configured or its proposal is unusable.
    /// </summary>
    public async Task<GenerationResult> GenerateAsync(
        IReadOnlyList<Recipe> pool, DietIntent intent, double kcalTarget, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(intent);

        if (Layout(pool, intent, kcalTarget) is not { } layout)
        {
            return EmptyPoolResult();
        }

        var slots = await TryAgentSelectAsync(pool, intent, layout, ct).ConfigureAwait(false)
            ?? GreedySelect(pool, layout);
        return Finalize(slots, intent, kcalTarget, layout.NumBlocks);
    }

    private static GenerationResult EmptyPoolResult() => new(
        false, [], Macros.Zero, "No recipes match your constraints — relax the diet/prep time or add recipes.");

    // The plan shape: meals/day, blocks, and the per-meal kcal budget. Null when the pool is empty.
    // Day-block rotation: cook one distinct meal-set per block, not per calendar day. blockSize=1 ⇒
    // numBlocks=HorizonDays ⇒ a fresh set every day (the original behaviour).
    private static PlanLayout? Layout(IReadOnlyList<Recipe> pool, DietIntent intent, double kcalTarget)
    {
        if (pool.Count == 0)
        {
            return null;
        }

        var mealsPerDay = Math.Clamp(intent.MealsPerDay, 1, 4);
        var blockSize = Math.Clamp(intent.BlockSize, 1, Math.Max(intent.HorizonDays, 1));
        var numBlocks = (int)Math.Ceiling(Math.Max(intent.HorizonDays, 1) / (double)blockSize);
        var perMeal = kcalTarget / mealsPerDay; // mealsPerDay is clamped ≥ 1
        return new PlanLayout(mealsPerDay, numBlocks, perMeal);
    }

    private readonly record struct PlanLayout(int MealsPerDay, int NumBlocks, double PerMeal);

    // VERIFY → REPAIR → VERIFY → allergen re-check → explain. Shared by both SELECT strategies, so the
    // agent path inherits every safety guarantee unchanged.
    private GenerationResult Finalize(List<PlannedSlot> slots, DietIntent intent, double kcalTarget, int numBlocks)
    {
        var (ok, avg) = PlanVerifier.Verify(slots, kcalTarget, numBlocks);
        if (!ok)
        {
            // REPAIR: solve for servings that hit the daily targets (each block is one day-group).
            var optimizerSlots = slots.Select(s => new OptimizerSlot(s.Day, s.Recipe.KcalPerServing, s.Recipe.ProteinPerServing)).ToList();
            var servings = optimizer.Optimize(optimizerSlots, kcalTarget, intent.ProteinTarget, 0.5, 3.0);
            for (var i = 0; i < slots.Count && i < servings.Count; i++)
            {
                slots[i].Servings = Math.Round(servings[i], 2);
            }

            (ok, avg) = PlanVerifier.Verify(slots, kcalTarget, numBlocks);
        }

        // Final defense-in-depth allergen re-check — enforced regardless of who chose the recipes.
        if (!PlanVerifier.AllergensClear(slots, intent.ExcludeKeywords))
        {
            return new GenerationResult(false, [], avg, "Could not build an allergen-safe plan from the available recipes.");
        }

        if (!ok)
        {
            return new GenerationResult(false, slots, avg,
                $"Closest plan averages {avg.Kcal:0} kcal/day vs your {kcalTarget:0} target — add recipes or relax constraints.");
        }

        return new GenerationResult(true, slots, avg, Explain(avg, kcalTarget, intent));
    }

    // Ask the agent (if configured) for a selection and turn it into slots. Returns null — meaning
    // "fall back to greedy" — when there is no agent, it isn't configured, it throws, or the proposal
    // doesn't fully + validly cover the grid from the pool.
    private async Task<List<PlannedSlot>?> TryAgentSelectAsync(
        IReadOnlyList<Recipe> pool, DietIntent intent, PlanLayout layout, CancellationToken ct)
    {
        if (selectAgent is null || !selectAgent.IsConfigured)
        {
            return null;
        }

        IReadOnlyList<MealSelection>? picks;
        try
        {
            picks = await selectAgent.SelectAsync(pool, intent, layout.NumBlocks, layout.MealsPerDay, ct).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // any agent failure must degrade to deterministic selection, never fail generation
        catch (Exception)
        {
            return null;
        }
#pragma warning restore CA1031

        if (picks is null || picks.Count == 0)
        {
            return null;
        }

        var byId = pool.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        var bySlot = new Dictionary<(int Block, MealSlot Meal), Recipe>();
        foreach (var p in picks)
        {
            if (byId.TryGetValue(p.RecipeId, out var recipe))
            {
                bySlot[(p.Block, p.Meal)] = recipe; // ignore picks for recipes outside the filtered pool
            }
        }

        var meals = AllMeals.Take(layout.MealsPerDay).ToArray();
        var slots = new List<PlannedSlot>();
        for (var block = 1; block <= layout.NumBlocks; block++)
        {
            foreach (var meal in meals)
            {
                if (!bySlot.TryGetValue((block, meal), out var recipe))
                {
                    return null; // an incomplete proposal ⇒ fall back wholesale, don't half-fill
                }

                slots.Add(new PlannedSlot { Day = block, Meal = meal, Recipe = recipe, Servings = InitialServings(recipe, layout.PerMeal) });
            }
        }

        return slots;
    }

    // One distinct meal-set per BLOCK (Day = block index 1..numBlocks). With numBlocks == HorizonDays
    // (blockSize 1) this is the original per-day selection.
    private static List<PlannedSlot> GreedySelect(IReadOnlyList<Recipe> pool, PlanLayout layout)
    {
        var meals = AllMeals.Take(layout.MealsPerDay).ToArray();
        var slots = new List<PlannedSlot>();
        var index = 0;

        for (var block = 1; block <= layout.NumBlocks; block++)
        {
            foreach (var meal in meals)
            {
                var recipe = pool[index % pool.Count];
                index++;
                slots.Add(new PlannedSlot { Day = block, Meal = meal, Recipe = recipe, Servings = InitialServings(recipe, layout.PerMeal) });
            }
        }

        return slots;
    }

    private static double InitialServings(Recipe recipe, double perMeal)
    {
        var raw = recipe.KcalPerServing > 0 ? perMeal / recipe.KcalPerServing : 1;
        return Math.Clamp(Math.Round(raw * 2, MidpointRounding.AwayFromZero) / 2, 0.5, 3.0); // nearest 0.5
    }

    private static string Explain(Macros avg, double kcalTarget, DietIntent intent)
    {
        var withinPct = kcalTarget > 0 ? Math.Abs(avg.Kcal - kcalTarget) / kcalTarget * 100 : 0;
        var diet = string.IsNullOrEmpty(intent.DietSlug) ? "" : $", all {intent.DietSlug}";
        var prep = intent.MaxPrepMinutes is { } p ? $", nothing over {p} min" : "";
        var blockSize = Math.Clamp(intent.BlockSize, 1, Math.Max(intent.HorizonDays, 1));
        var numBlocks = (int)Math.Ceiling(Math.Max(intent.HorizonDays, 1) / (double)blockSize);
        var batch = blockSize > 1 ? $", cooked as {numBlocks} batch{(numBlocks == 1 ? "" : "es")} of up to {blockSize} days" : "";
        return $"Your {intent.HorizonDays}-day plan averages {avg.Kcal:0} kcal/day "
            + $"(within {withinPct:0.#}% of your {kcalTarget:0} target), protein {avg.ProteinG:0} g{diet}{prep}{batch}.";
    }
}
