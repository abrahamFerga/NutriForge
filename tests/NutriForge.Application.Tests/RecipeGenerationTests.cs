using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Recipes;

namespace NutriForge.Application.Tests;

/// <summary>
/// AI recipe generation (#101 slice 2): the agent writes recipe structure; the service persists each via
/// RecipeService, which resolves nutrition deterministically (catalog ∪ reference). No live model — a
/// fake agent returns canned recipes so the build→resolve→persist path is exercised end to end.
/// </summary>
public sealed class RecipeGenerationTests
{
    private sealed class FakeRecipeGenAgent(IReadOnlyList<ExtractedRecipe> canned, bool configured = true) : IRecipeGenAgent
    {
        public bool IsConfigured => configured;
        public Task<IReadOnlyList<ExtractedRecipe>> GenerateAsync(RecipeBrief brief, int count, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExtractedRecipe>>([.. canned.Take(count)]);
    }

    private static readonly RecipeBrief Brief = new("dinner", "high-protein", 600, 30, null, null, 2);

    [Fact]
    public async Task Generates_persists_and_computes_nutrition_without_a_catalog()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var recipes = new RecipeService(db, new FakeCurrentUser(userId));

        var canned = new List<ExtractedRecipe>
        {
            new("Grilled Chicken & Rice", 2, 25, "1. Season chicken. 2. Grill. 3. Serve over rice.",
                ["dinner", "high-protein"],
                [
                    new("200 g chicken breast", "chicken breast", 200, "g"),
                    new("150 g white rice", "white rice", 150, "g"),
                    new("1 tbsp olive oil", "olive oil", 1, "tbsp"),
                ]),
            new("Veggie Egg Scramble", 1, 10, "1. Whisk eggs. 2. Scramble with spinach.",
                ["breakfast"],
                [
                    new("2 eggs", "egg", 2, null),
                    new("50 g spinach", "spinach", 50, "g"),
                ]),
        };

        var gen = new RecipeGenerationService(recipes, new FakeRecipeGenAgent(canned));
        var created = await gen.GenerateAsync(userId, Brief, count: 5);

        Assert.Equal(2, created.Count);
        Assert.All(created, r => Assert.True(r.IsNutritionComputed, $"'{r.Name}' should compute nutrition"));
        Assert.True(created[0].KcalPerServing > 0);
        Assert.Equal("ai-generated", created[0].SourceType);

        // Persisted and owned by the user (visible via the catalog).
        Assert.Equal(2, await db.Recipes.IgnoreQueryFilters().CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Respects_the_requested_count()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var recipes = new RecipeService(db, new FakeCurrentUser(userId));

        var canned = Enumerable.Range(0, 5).Select(i => new ExtractedRecipe(
            $"Bowl {i}", 1, 10, "Mix.", ["lunch"],
            [new("100 g chicken breast", "chicken breast", 100, "g")])).ToList();

        var gen = new RecipeGenerationService(recipes, new FakeRecipeGenAgent(canned));
        var created = await gen.GenerateAsync(userId, Brief, count: 2);

        Assert.Equal(2, created.Count);
    }

    [Fact]
    public async Task Returns_empty_when_no_ai_provider_is_configured()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var recipes = new RecipeService(db, new FakeCurrentUser(userId));
        var gen = new RecipeGenerationService(recipes, new FakeRecipeGenAgent([], configured: false));

        Assert.False(gen.IsConfigured);
        Assert.Empty(await gen.GenerateAsync(userId, Brief, count: 3));
    }
}
