using NutriForge.Application.DietGen;
using NutriForge.Domain.Common;
using NutriForge.Domain.Recipes;
using NutriForge.Infrastructure.DietGen;

namespace NutriForge.Application.Tests;

/// <summary>
/// Epic 6 — the diet-generation trust anchors: deterministic FILTER (allergen exclusion), the
/// OR-Tools LP REPAIR, end-to-end generation within tolerance, and the defense-in-depth allergen
/// re-check. The LLM is never involved in these numbers.
/// </summary>
public sealed class DietGenerationTests
{
    private static Recipe Rec(string name, string[] tags, params (string Ingredient, Macros Macros)[] ingredients)
    {
        var r = new Recipe { Name = name, Servings = 1, TotalMinutes = 15, Tags = [.. tags] };
        foreach (var (ing, m) in ingredients)
        {
            var ri = new RecipeIngredient { IngredientName = ing, Quantity = 100, Unit = "g" };
            ri.SetContribution(100, m);
            r.AddIngredient(ri);
        }

        r.RecomputeNutrition();
        return r;
    }

    [Fact]
    public void Filter_enforces_diet_tags_and_excludes_disallowed_ingredients()
    {
        var tofu = Rec("Tofu bowl", ["vegan", "high-protein"], ("tofu", new Macros(200, 20, 8, 12)), ("rice", new Macros(200, 4, 1, 44)));
        var chicken = Rec("Chicken & rice", ["high-protein"], ("chicken breast", new Macros(200, 35, 4, 0)), ("rice", new Macros(200, 4, 1, 44)));
        var diet = new DietType { Slug = "vegan", RequiredTags = ["vegan"], ExcludedKeywords = ["chicken"] };
        var intent = new DietIntent(2000, null, "vegan", [], null, 3, 7);

        var pool = RecipeFilter.Filter([tofu, chicken], intent, diet);

        Assert.Single(pool);
        Assert.Equal("Tofu bowl", pool[0].Name);
    }

    [Fact]
    public void Filter_excludes_recipes_containing_a_declared_allergen()
    {
        var safe = Rec("Rice bowl", [], ("rice", new Macros(300, 6, 1, 66)));
        var peanut = Rec("Peanut stew", [], ("peanut", new Macros(400, 16, 34, 14)));
        var intent = new DietIntent(2000, null, null, ["peanut"], null, 3, 7);

        var pool = RecipeFilter.Filter([safe, peanut], intent, diet: null);

        Assert.DoesNotContain(pool, r => r.Name == "Peanut stew");
    }

    [Fact]
    public void OrTools_repair_solves_servings_to_hit_the_daily_calorie_target()
    {
        var optimizer = new OrToolsPortionOptimizer();
        // Day 1: two 500-kcal-per-serving meals. To hit 2000 kcal the servings must total 4.
        var slots = new List<OptimizerSlot> { new(1, 500, 30), new(1, 500, 30) };

        var servings = optimizer.Optimize(slots, dailyKcalTarget: 2000, dailyProteinTarget: null, minServings: 0.5, maxServings: 3.0);

        var dayKcal = (500 * servings[0]) + (500 * servings[1]);
        Assert.Equal(2000, dayKcal, precision: 0);
        Assert.All(servings, s => Assert.InRange(s, 0.5, 3.0));
    }

    [Fact]
    public void Generate_produces_a_full_plan_within_tolerance_of_the_target()
    {
        var generator = new DietPlanGenerator(new OrToolsPortionOptimizer());
        var pool = new[]
        {
            Rec("Vegan bowl A", ["vegan"], ("tofu", new Macros(400, 30, 12, 30))),
            Rec("Vegan bowl B", ["vegan"], ("beans", new Macros(600, 25, 10, 80))),
            Rec("Vegan bowl C", ["vegan"], ("oats", new Macros(500, 18, 9, 70))),
        };
        var intent = new DietIntent(2000, null, "vegan", [], null, MealsPerDay: 3, HorizonDays: 7);

        var result = generator.Generate(pool, intent, kcalTarget: 2000);

        Assert.True(result.Feasible, result.Message);
        Assert.Equal(21, result.Slots.Count); // 7 days × 3 meals
        Assert.True(Math.Abs(result.DailyAverage.Kcal - 2000) <= 100, $"avg {result.DailyAverage.Kcal}");
    }

    [Fact]
    public void Generate_returns_infeasible_when_no_recipes_match()
    {
        var generator = new DietPlanGenerator(new OrToolsPortionOptimizer());
        var intent = new DietIntent(2000, null, "vegan", [], null, 3, 7);

        var result = generator.Generate([], intent, 2000);

        Assert.False(result.Feasible);
        Assert.Contains("No recipes", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_re_checks_allergens_and_refuses_a_plan_with_an_excluded_ingredient()
    {
        // Defense in depth: even if an unsafe recipe reached the generator, the final allergen
        // re-check must refuse it — the LLM/SELECT can never sneak a declared allergen through.
        var generator = new DietPlanGenerator(new OrToolsPortionOptimizer());
        var mushroom = Rec("Mushroom risotto", [], ("mushroom", new Macros(500, 12, 14, 70)));
        var intent = new DietIntent(2000, null, null, ["mushroom"], null, 3, 7);

        var result = generator.Generate([mushroom], intent, 2000);

        Assert.False(result.Feasible);
        Assert.Contains("allergen-safe", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
