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

    public static (bool Ok, Macros DailyAverage) Verify(IReadOnlyList<PlannedSlot> slots, double kcalTarget, int horizonDays)
    {
        var days = Math.Max(horizonDays, 1);
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
/// The hybrid generate-and-check generator: greedy SELECT (deterministic; an LLM agent can replace
/// it later) → VERIFY → LP REPAIR → VERIFY. The LLM never owns a number; this is all deterministic.
/// </summary>
public sealed class DietPlanGenerator(IPortionOptimizer optimizer)
{
    private static readonly MealSlot[] AllMeals = [MealSlot.Breakfast, MealSlot.Lunch, MealSlot.Dinner, MealSlot.Snack];

    public GenerationResult Generate(IReadOnlyList<Recipe> pool, DietIntent intent, double kcalTarget)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(intent);

        var mealsPerDay = Math.Clamp(intent.MealsPerDay, 1, 4);
        if (pool.Count == 0)
        {
            return new GenerationResult(false, [], Macros.Zero,
                "No recipes match your constraints — relax the diet/prep time or add recipes.");
        }

        var slots = GreedySelect(pool, intent, kcalTarget, mealsPerDay);

        var (ok, avg) = PlanVerifier.Verify(slots, kcalTarget, intent.HorizonDays);
        if (!ok)
        {
            // REPAIR: solve for servings that hit the daily targets.
            var optimizerSlots = slots.Select(s => new OptimizerSlot(s.Day, s.Recipe.KcalPerServing, s.Recipe.ProteinPerServing)).ToList();
            var servings = optimizer.Optimize(optimizerSlots, kcalTarget, intent.ProteinTarget, 0.5, 3.0);
            for (var i = 0; i < slots.Count && i < servings.Count; i++)
            {
                slots[i].Servings = Math.Round(servings[i], 2);
            }

            (ok, avg) = PlanVerifier.Verify(slots, kcalTarget, intent.HorizonDays);
        }

        // Final defense-in-depth allergen re-check.
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

    private static List<PlannedSlot> GreedySelect(IReadOnlyList<Recipe> pool, DietIntent intent, double kcalTarget, int mealsPerDay)
    {
        var meals = AllMeals.Take(mealsPerDay).ToArray();
        var perMeal = kcalTarget / mealsPerDay;
        var slots = new List<PlannedSlot>();
        var index = 0;

        for (var day = 1; day <= intent.HorizonDays; day++)
        {
            foreach (var meal in meals)
            {
                var recipe = pool[index % pool.Count];
                index++;
                var raw = recipe.KcalPerServing > 0 ? perMeal / recipe.KcalPerServing : 1;
                var servings = Math.Clamp(Math.Round(raw * 2, MidpointRounding.AwayFromZero) / 2, 0.5, 3.0); // nearest 0.5
                slots.Add(new PlannedSlot { Day = day, Meal = meal, Recipe = recipe, Servings = servings });
            }
        }

        return slots;
    }

    private static string Explain(Macros avg, double kcalTarget, DietIntent intent)
    {
        var withinPct = kcalTarget > 0 ? Math.Abs(avg.Kcal - kcalTarget) / kcalTarget * 100 : 0;
        var diet = string.IsNullOrEmpty(intent.DietSlug) ? "" : $", all {intent.DietSlug}";
        var prep = intent.MaxPrepMinutes is { } p ? $", nothing over {p} min" : "";
        return $"Your {intent.HorizonDays}-day plan averages {avg.Kcal:0} kcal/day "
            + $"(within {withinPct:0.#}% of your {kcalTarget:0} target), protein {avg.ProteinG:0} g{diet}{prep}.";
    }
}
