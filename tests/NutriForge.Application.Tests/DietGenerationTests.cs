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
    public void Generate_with_day_blocks_emits_one_meal_set_per_block_and_keeps_the_per_day_average()
    {
        var generator = new DietPlanGenerator(new OrToolsPortionOptimizer());
        var pool = new[]
        {
            Rec("Vegan bowl A", ["vegan"], ("tofu", new Macros(400, 30, 12, 30))),
            Rec("Vegan bowl B", ["vegan"], ("beans", new Macros(600, 25, 10, 80))),
            Rec("Vegan bowl C", ["vegan"], ("oats", new Macros(500, 18, 9, 70))),
        };
        // 6-day horizon cooked as two 3-day blocks ⇒ two distinct meal-sets, not six.
        var intent = new DietIntent(2000, null, "vegan", [], null, MealsPerDay: 3, HorizonDays: 6, BlockSize: 3);

        var result = generator.Generate(pool, intent, kcalTarget: 2000);

        Assert.True(result.Feasible, result.Message);
        Assert.Equal(6, result.Slots.Count); // 2 blocks × 3 meals (NOT 6 days × 3)
        Assert.Equal([1, 2], result.Slots.Select(s => s.Day).Distinct().Order().ToArray());
        // The daily average is per-DAY and stays on target regardless of how days are blocked.
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

    // ---- SELECT agent (#36) ----

    private static Recipe[] VeganPool() =>
    [
        Rec("Vegan bowl A", ["vegan"], ("tofu", new Macros(400, 30, 12, 30))),
        Rec("Vegan bowl B", ["vegan"], ("beans", new Macros(600, 25, 10, 80))),
        Rec("Vegan bowl C", ["vegan"], ("oats", new Macros(500, 18, 9, 70))),
    ];

    private static List<MealSelection> FullSelection(
        IReadOnlyList<Recipe> pool, int numBlocks, int mealsPerDay, Func<Recipe> pick)
    {
        var meals = new[] { MealSlot.Breakfast, MealSlot.Lunch, MealSlot.Dinner, MealSlot.Snack }.Take(mealsPerDay).ToArray();
        var list = new List<MealSelection>();
        for (var block = 1; block <= numBlocks; block++)
        {
            foreach (var meal in meals)
            {
                list.Add(new MealSelection(block, meal, pick().Id));
            }
        }

        return list;
    }

    [Fact]
    public async Task GenerateAsync_uses_the_agent_selection_when_it_covers_the_grid()
    {
        var pool = VeganPool();
        var chosen = pool[^1]; // the agent picks the SAME recipe everywhere — greedy would rotate all three.
        var agent = new FakeSelectAgent(configured: true, (p, blocks, meals) => FullSelection(p, blocks, meals, () => chosen));
        var generator = new DietPlanGenerator(new OrToolsPortionOptimizer(), agent);
        var intent = new DietIntent(2000, null, "vegan", [], null, MealsPerDay: 3, HorizonDays: 7);

        var result = await generator.GenerateAsync(pool, intent, 2000);

        Assert.True(result.Feasible, result.Message);
        Assert.Equal(21, result.Slots.Count);
        Assert.All(result.Slots, s => Assert.Equal(chosen.Name, s.Recipe.Name));
    }

    [Fact]
    public async Task GenerateAsync_falls_back_to_greedy_when_the_agent_proposal_is_unusable()
    {
        var pool = VeganPool();
        // A single pick for an unknown recipe id can't complete the grid ⇒ fall back wholesale.
        var agent = new FakeSelectAgent(configured: true,
            (_, _, _) => [new MealSelection(1, MealSlot.Breakfast, Guid.NewGuid())]);
        var generator = new DietPlanGenerator(new OrToolsPortionOptimizer(), agent);
        var intent = new DietIntent(2000, null, "vegan", [], null, MealsPerDay: 3, HorizonDays: 7);

        var viaAgent = await generator.GenerateAsync(pool, intent, 2000);
        var greedy = generator.Generate(pool, intent, 2000);

        Assert.Equal(
            greedy.Slots.Select(s => s.Recipe.Name).ToArray(),
            viaAgent.Slots.Select(s => s.Recipe.Name).ToArray());
        Assert.Equal(greedy.Feasible, viaAgent.Feasible);
    }

    [Fact]
    public async Task GenerateAsync_refuses_an_allergen_unsafe_recipe_even_when_the_agent_picks_it()
    {
        // Defense in depth survives the agent path: the agent actively chooses an excluded recipe, and
        // the final allergen re-check still refuses the plan.
        var mushroom = Rec("Mushroom risotto", [], ("mushroom", new Macros(500, 12, 14, 70)));
        var agent = new FakeSelectAgent(configured: true, (p, blocks, meals) => FullSelection(p, blocks, meals, () => mushroom));
        var generator = new DietPlanGenerator(new OrToolsPortionOptimizer(), agent);
        var intent = new DietIntent(2000, null, null, ["mushroom"], null, MealsPerDay: 3, HorizonDays: 7);

        var result = await generator.GenerateAsync([mushroom], intent, 2000);

        Assert.False(result.Feasible);
        Assert.Contains("allergen-safe", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateAsync_without_a_configured_agent_matches_deterministic_generation()
    {
        var pool = VeganPool();
        var intent = new DietIntent(2000, null, "vegan", [], null, MealsPerDay: 3, HorizonDays: 7);
        var generator = new DietPlanGenerator(new OrToolsPortionOptimizer(), new FakeSelectAgent(configured: false));

        var viaAsync = await generator.GenerateAsync(pool, intent, 2000);
        var sync = generator.Generate(pool, intent, 2000);

        Assert.Equal(sync.Slots.Select(s => s.Recipe.Name), viaAsync.Slots.Select(s => s.Recipe.Name));
        Assert.Equal(sync.Feasible, viaAsync.Feasible);
        Assert.Equal(sync.DailyAverage.Kcal, viaAsync.DailyAverage.Kcal, precision: 3);
    }

    // ---- Meal-type enforcement (#101 breakfast fix) ----

    private static Recipe[] MealTaggedPool() =>
    [
        Rec("Oatmeal", ["breakfast"], ("oats", new Macros(400, 15, 8, 60))),
        Rec("Veggie omelette", ["breakfast"], ("eggs", new Macros(350, 24, 25, 4))),
        Rec("Chicken salad", ["lunch"], ("chicken", new Macros(500, 40, 20, 10))),
        Rec("Steak & greens", ["dinner"], ("beef", new Macros(700, 45, 40, 5))),
        Rec("Trail mix", ["snack"], ("nuts", new Macros(200, 6, 16, 8))),
    ];

    [Fact]
    public void Generate_keeps_each_meal_slot_to_its_meal_type()
    {
        // The deterministic selector must never put a dinner recipe in a breakfast slot: each slot only
        // draws from recipes carrying that slot's meal-type tag. (Round-robin over the flat pool — the
        // old behaviour — would scatter recipes across the wrong slots and fail this.)
        var generator = new DietPlanGenerator(new OrToolsPortionOptimizer());
        var intent = new DietIntent(2000, null, null, [], null, MealsPerDay: 4, HorizonDays: 3);

        var result = generator.Generate(MealTaggedPool(), intent, 2000);

        Assert.True(result.Feasible, result.Message);
        Assert.All(result.Slots, s =>
            Assert.Contains(s.Meal.ToString().ToLowerInvariant(), s.Recipe.Tags, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GenerateAsync_rejects_a_meal_type_mismatched_agent_pick_and_keeps_slots_on_type()
    {
        // The agent deliberately puts the DINNER recipe in every slot (including breakfast/lunch). Those
        // meal-type-mismatched picks are rejected, the grid is incomplete, and we fall back to the
        // meal-aware greedy selection — so every slot still holds a recipe of its own meal type.
        var pool = MealTaggedPool();
        var dinner = pool.Single(r => r.Tags.Contains("dinner"));
        var agent = new FakeSelectAgent(configured: true, (p, blocks, meals) => FullSelection(p, blocks, meals, () => dinner));
        var generator = new DietPlanGenerator(new OrToolsPortionOptimizer(), agent);
        var intent = new DietIntent(2000, null, null, [], null, MealsPerDay: 4, HorizonDays: 2);

        var result = await generator.GenerateAsync(pool, intent, 2000);

        Assert.True(result.Feasible, result.Message);
        Assert.All(result.Slots, s =>
            Assert.Contains(s.Meal.ToString().ToLowerInvariant(), s.Recipe.Tags, StringComparer.OrdinalIgnoreCase));
    }

    private sealed class FakeSelectAgent(
        bool configured,
        Func<IReadOnlyList<Recipe>, int, int, IReadOnlyList<MealSelection>?>? select = null) : IMealSelectAgent
    {
        public bool IsConfigured => configured;

        public Task<IReadOnlyList<MealSelection>?> SelectAsync(
            IReadOnlyList<Recipe> pool, DietIntent intent, int numBlocks, int mealsPerDay, CancellationToken ct = default)
            => Task.FromResult(select?.Invoke(pool, numBlocks, mealsPerDay));
    }
}
