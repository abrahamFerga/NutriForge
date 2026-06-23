using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NutriForge.Application.DietGen;
using NutriForge.Infrastructure.DietGen;
using NutriForge.Infrastructure.Persistence;

namespace NutriForge.Application.Tests;

/// <summary>Replicates the diet-plan worker's generation step against seeded data to localize bugs.</summary>
public sealed class DietGenWorkerLogicTests
{
    [Fact]
    public async Task Worker_generation_logic_produces_a_ready_plan_from_seeded_recipes()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        await DevDataSeeder.SeedAsync(db, CancellationToken.None);

        // The worker's exact steps:
        var intent = new DietIntent(2345, 176, "high-protein", [], null, 3, 3);
        var recipes = await db.Recipes.IgnoreQueryFilters().Include(r => r.Ingredients)
            .Where(r => r.IsNutritionComputed).ToListAsync(CancellationToken.None);
        Assert.NotEmpty(recipes);

        var diet = await db.DietTypes.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Slug == "high-protein", CancellationToken.None);
        Assert.NotNull(diet);

        var pool = RecipeFilter.Filter(recipes, intent, diet);
        Assert.NotEmpty(pool);

        var generator = new DietPlanGenerator(new OrToolsPortionOptimizer());
        var result = generator.Generate(pool, intent, 2345);

        Assert.True(result.Feasible, result.Message);
        Assert.Equal(9, result.Slots.Count); // 3 days × 3 meals

        // Round-trip the intent the way the service/worker do (STJ records).
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(intent, opts);
        var back = JsonSerializer.Deserialize<DietIntent>(json, opts);
        Assert.NotNull(back);
        Assert.Equal("high-protein", back!.DietSlug);
    }
}
