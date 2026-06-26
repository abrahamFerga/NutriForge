using NutriForge.Application.Recipes;
using NutriForge.Infrastructure.Persistence;

namespace NutriForge.Application.Tests;

/// <summary>
/// The deterministic nutrition reference (#101): common ingredients the catalog doesn't carry still
/// resolve to real macros from a curated table (never the LLM), so AI-generated / freely-imported recipes
/// compute their nutrition instead of being stuck at 0 kcal (the "Chicken Fajita Meal Prep" import bug).
/// </summary>
public sealed class NutritionReferenceTests
{
    [Fact]
    public void Resolves_a_mass_ingredient_to_per_100g_scaled_macros()
    {
        var hit = NutritionReference.Resolve("chicken breast", 150, "g");

        Assert.NotNull(hit);
        Assert.Equal(150, hit!.Value.Grams);
        Assert.Equal(248, hit.Value.Macros.Kcal); // 165 kcal/100g × 1.5
        Assert.True(hit.Value.Macros.ProteinG >= 45);
    }

    [Fact]
    public void Resolves_a_volume_ingredient_via_density()
    {
        // 1 tbsp olive oil = 15 ml × 0.91 g/ml ≈ 13.65 g → ~121 kcal.
        var hit = NutritionReference.Resolve("olive oil", 1, "tbsp");

        Assert.NotNull(hit);
        Assert.InRange(hit!.Value.Grams, 13, 14);
        Assert.InRange(hit.Value.Macros.Kcal, 115, 125);
    }

    [Fact]
    public void Resolves_a_count_ingredient_via_grams_per_count()
    {
        // "2 limes" with no unit → 2 × 67 g.
        var hit = NutritionReference.Resolve("lime", 2, null);

        Assert.NotNull(hit);
        Assert.Equal(134, hit!.Value.Grams);
    }

    [Theory]
    [InlineData("paprika")]
    [InlineData("onion powder")]
    [InlineData("ground cumin")]
    [InlineData("chilli powder")]
    [InlineData("cornstarch")]
    [InlineData("sea salt flakes")]
    [InlineData("red bell pepper")]
    [InlineData("yellow bell pepper")]
    [InlineData("fresh oregano")]
    [InlineData("avocado")]
    [InlineData("jalapeno")]
    [InlineData("coriander")]
    [InlineData("lime wedge")]
    [InlineData("garlic clove")]
    [InlineData("chicken stock")]
    public void Every_chicken_fajita_ingredient_is_known(string name) =>
        Assert.True(NutritionReference.IsKnown(name), $"'{name}' should resolve from the reference table");

    [Fact]
    public void Prefers_the_more_specific_token()
    {
        // "garlic powder" must not collapse to plain "garlic" (very different macros).
        var powder = NutritionReference.Resolve("garlic powder", 100, "g")!.Value;
        var clove = NutritionReference.Resolve("garlic clove", 100, "g")!.Value;
        Assert.True(powder.Macros.CarbG > 60); // powder ≈ 73 g carb/100g
        Assert.True(clove.Macros.Kcal < 200); // fresh garlic ≈ 149 kcal/100g
    }

    [Fact]
    public void Salt_is_known_and_contributes_no_energy()
    {
        var hit = NutritionReference.Resolve("sea salt", 1, "tsp");
        Assert.NotNull(hit);
        Assert.Equal(0, hit!.Value.Macros.Kcal);
    }

    [Fact]
    public void Unknown_ingredient_returns_null()
    {
        Assert.Null(NutritionReference.Resolve("zorblax pseudofruit", 1, "g"));
        Assert.False(NutritionReference.IsKnown("zorblax pseudofruit"));
    }

    [Fact]
    public async Task A_fajita_recipe_with_no_catalog_ingredients_now_computes_nutrition()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        // NB: no canonical Ingredients seeded — resolution must come entirely from the reference table.

        var recipes = new RecipeService(db, new FakeCurrentUser(userId));
        var recipe = await recipes.CreateAsync(new CreateRecipeRequest(
            "Chicken Fajita Meal Prep", Servings: 5, TotalMinutes: 40, Instructions: "Cook and portion.",
            Tags: ["meal prep", "chicken", "mexican"],
            Ingredients:
            [
                new RecipeIngredientInput(800, "g", "chicken breast"),
                new RecipeIngredientInput(2, "tbsp", "olive oil"),
                new RecipeIngredientInput(2, "tsp", "paprika"),
                new RecipeIngredientInput(1, "tsp", "ground cumin"),
                new RecipeIngredientInput(1, "tsp", "garlic powder"),
                new RecipeIngredientInput(1, "tsp", "onion powder"),
                new RecipeIngredientInput(1, "tsp", "chilli powder"),
                new RecipeIngredientInput(1, null, "red bell pepper"),
                new RecipeIngredientInput(1, null, "yellow bell pepper"),
                new RecipeIngredientInput(1, null, "brown onion"),
                new RecipeIngredientInput(2, null, "lime"),
                new RecipeIngredientInput(0, null, "coriander"), // garnish, unquantified — must not block computing
            ]), ownerUserId: userId);

        Assert.True(recipe.IsNutritionComputed, "all ingredients resolve via the reference table");
        Assert.True(recipe.KcalPerServing > 100, $"expected a real per-serving figure, got {recipe.KcalPerServing}");
    }
}
