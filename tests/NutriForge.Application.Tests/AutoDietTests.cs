using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Application.DietGen;
using NutriForge.Application.Planning;
using NutriForge.Application.Recipes;
using NutriForge.Application.Tracking;
using NutriForge.Domain.Recipes;
using NutriForge.Infrastructure.DietGen;
using NutriForge.Infrastructure.Persistence;

namespace NutriForge.Application.Tests;

/// <summary>
/// The headline "make me a full diet" flow (#101): the agent writes a fresh, varied set of recipes FOR
/// the plan and the planner builds the week from EXACTLY those — it never reads the user's recipe catalog
/// and never pollutes it (the recipes are tagged plan-generated and hidden from the Recipes list).
/// No live model — a fake agent supplies recipes.
/// </summary>
public sealed class AutoDietTests
{
    private static readonly IClock Clock = new FakeClock(new DateTimeOffset(2026, 6, 26, 12, 0, 0, TimeSpan.Zero));

    private sealed class NoOpIntentParser : IDietIntentParser
    {
        public bool IsConfigured => false;
        public Task<DietIntent> ParseAsync(string desire, DietIntent defaults, CancellationToken ct = default) =>
            Task.FromResult(defaults);
    }

    /// <summary>Returns `count` distinct chicken+rice recipes per call (resolve via the reference table).</summary>
    private sealed class CountingGenAgent(bool configured = true) : IRecipeGenAgent
    {
        private int _n;
        public bool IsConfigured => configured;

        public Task<IReadOnlyList<ExtractedRecipe>> GenerateAsync(RecipeBrief brief, int count, CancellationToken ct = default)
        {
            var list = Enumerable.Range(0, count).Select(_ =>
            {
                var id = ++_n;
                return new ExtractedRecipe($"Fresh {brief.MealType} bowl {id}", 2, 20, "1. Cook chicken. 2. Serve over rice.",
                    ["dinner"],
                    [
                        new("200 g chicken breast", "chicken breast", 200, "g"),
                        new("150 g white rice", "white rice", 150, "g"),
                    ]);
            }).ToList();
            return Task.FromResult<IReadOnlyList<ExtractedRecipe>>(list);
        }
    }

    private static AutoDietService BuildAuto(NutriForgeDbContext db, Guid userId, IRecipeGenAgent agent)
    {
        var targets = new TargetService(db, Clock);
        var profiles = new ProfileService(db, targets);
        var generator = new DietPlanGenerator(new OrToolsPortionOptimizer());
        var shopping = new ShoppingListService(db, db);
        var plans = new DietPlanService(db, db, targets, profiles, new NoOpIntentParser(), generator, shopping, Clock);
        var recipeGen = new RecipeGenerationService(new RecipeService(db, new FakeCurrentUser(userId)), agent);
        return new AutoDietService(plans, recipeGen, targets, profiles);
    }

    private static void SeedDiet(NutriForgeDbContext db, string slug) =>
        db.DietTypes.Add(new DietType { Slug = slug, Name = slug, RequiredTags = [slug], ExcludedKeywords = [] });

    private static CreateDietPlanRequest Req(string? diet) => new(
        Desire: null, KcalTarget: 2000, DietSlug: diet, ExcludeAllergens: null, Dislikes: null,
        MaxPrepMinutes: null, MealsPerDay: 3, HorizonDays: 3, Eaters: null, BlockSize: null, Members: []);

    [Fact]
    public async Task Writes_fresh_recipes_and_plans_from_them()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        SeedDiet(db, "high-protein");
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await BuildAuto(db, userId, new CountingGenAgent()).CreateAsync(userId, Req("high-protein"));

        Assert.True(result.RecipesGenerated > 0, "the agent should write recipes for the plan");
        Assert.NotEmpty(result.Plan.Slots);
        // Every meal in the plan is one of the freshly written recipes.
        Assert.All(result.Plan.Slots, s => Assert.StartsWith("Fresh ", s.RecipeName));
    }

    [Fact]
    public async Task Generated_recipes_carry_the_diet_tag_so_they_qualify()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        SeedDiet(db, "high-protein");
        await db.SaveChangesAsync(CancellationToken.None);

        await BuildAuto(db, userId, new CountingGenAgent()).CreateAsync(userId, Req("high-protein"));

        var recipes = await db.Recipes.IgnoreQueryFilters().ToListAsync(CancellationToken.None);
        Assert.NotEmpty(recipes);
        Assert.All(recipes, r => Assert.Contains("high-protein", r.Tags));
    }

    [Fact]
    public async Task Builds_from_fresh_recipes_not_the_existing_catalog()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        SeedDiet(db, "high-protein");
        await db.SaveChangesAsync(CancellationToken.None);

        // Pre-seed a deep, qualifying catalog. The auto flow must IGNORE these and use only fresh recipes.
        var recipes = new RecipeService(db, new FakeCurrentUser(userId));
        for (var i = 0; i < 12; i++)
        {
            await recipes.CreateAsync(new CreateRecipeRequest(
                $"Prebuilt {i}", 2, 15, "Cook.", ["high-protein"],
                [new RecipeIngredientInput(200, "g", "chicken breast"), new RecipeIngredientInput(150, "g", "white rice")]),
                ownerUserId: userId);
        }

        var result = await BuildAuto(db, userId, new CountingGenAgent()).CreateAsync(userId, Req("high-protein"));

        Assert.True(result.RecipesGenerated > 0, "fresh recipes are written even when the catalog is deep");
        Assert.NotEmpty(result.Plan.Slots);
        Assert.DoesNotContain(result.Plan.Slots, s => s.RecipeName.StartsWith("Prebuilt", StringComparison.Ordinal));
        Assert.All(result.Plan.Slots, s => Assert.StartsWith("Fresh ", s.RecipeName));
    }

    [Fact]
    public async Task Plan_generated_recipes_are_hidden_from_the_recipe_catalog()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        SeedDiet(db, "high-protein");
        await db.SaveChangesAsync(CancellationToken.None);

        var recipes = new RecipeService(db, new FakeCurrentUser(userId));
        // A recipe the user explicitly added to their catalog (should remain visible).
        await recipes.CreateAsync(new CreateRecipeRequest(
            "My own bowl", 1, 10, "Mix.", ["high-protein"],
            [new RecipeIngredientInput(150, "g", "chicken breast")]), ownerUserId: userId);

        await BuildAuto(db, userId, new CountingGenAgent()).CreateAsync(userId, Req("high-protein"));

        var listed = await recipes.ListAsync(null);
        Assert.Single(listed); // only the user's own recipe — the plan-generated ones are hidden
        Assert.Equal("My own bowl", listed[0].Name);
    }

    [Fact]
    public async Task Without_an_ai_provider_it_plans_from_the_catalog()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        await DevDataSeeder.SeedAsync(db, CancellationToken.None);

        var auto = BuildAuto(db, userId, new CountingGenAgent(configured: false));
        Assert.False(auto.IsConfigured);

        var result = await auto.CreateAsync(userId, Req("high-protein"));
        Assert.Equal(0, result.RecipesGenerated);
        Assert.NotNull(result.Plan);
    }
}
