using NutriForge.Application.Planning;
using NutriForge.Application.Recipes;
using NutriForge.Domain.Catalog;
using NutriForge.Domain.Common;
using NutriForge.Domain.Planning;
using NutriForge.Domain.Recipes;
using NutriForge.Infrastructure.Persistence;

namespace NutriForge.Application.Tests;

/// <summary>Epic 5 — computed-nutrition recipes and the consolidated, pantry-aware shopping list.</summary>
public sealed class RecipesAndShoppingTests
{
    private static Ingredient SeedFoodAndIngredient(
        NutriForgeDbContext db, string name, string aisle, double kcal, double protein, double fat, double carb)
    {
        var food = new Domain.Catalog.Food
        {
            Name = name,
            Source = new FoodSource("seed", name),
            VerificationStatus = VerificationStatus.Authoritative,
            NutrientProfile = new NutrientProfile { KcalPer100g = kcal, ProteinPer100g = protein, FatPer100g = fat, CarbPer100g = carb },
        };
        db.Foods.Add(food);
        var ingredient = new Ingredient { CanonicalName = name, AisleCategory = aisle, DefaultFoodId = food.Id };
        db.Ingredients.Add(ingredient);
        return ingredient;
    }

    [Fact]
    public async Task Recipe_nutrition_is_computed_from_resolved_ingredients()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        SeedFoodAndIngredient(db, "rice", "Pantry", 130, 2.7, 0.3, 28.2);
        await db.SaveChangesAsync(CancellationToken.None);

        var recipes = new RecipeService(db, new FakeCurrentUser(userId));
        var recipe = await recipes.CreateAsync(new CreateRecipeRequest(
            "Rice bowl", Servings: 2, TotalMinutes: 10, Instructions: null, Tags: ["dinner"],
            Ingredients: [new RecipeIngredientInput(300, "g", "rice")]), ownerUserId: userId);

        // 300 g rice @ 130 kcal/100g = 390 kcal total → 195 per serving (2 servings), computed by code.
        Assert.True(recipe.IsNutritionComputed);
        Assert.Equal(195, recipe.KcalPerServing);
    }

    [Fact]
    public async Task Shopping_list_consolidates_subtracts_pantry_and_groups_by_aisle()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var rice = SeedFoodAndIngredient(db, "rice", "Pantry", 130, 2.7, 0.3, 28.2);
        SeedFoodAndIngredient(db, "chicken", "Meat & Seafood", 120, 22.5, 2.6, 0);
        await db.SaveChangesAsync(CancellationToken.None);

        var recipes = new RecipeService(db, new FakeCurrentUser(userId));
        var bowl = await recipes.CreateAsync(new CreateRecipeRequest("Rice bowl", 2, 10, null, ["dinner"],
            [new RecipeIngredientInput(300, "g", "rice")]), ownerUserId: userId);
        var chickenRice = await recipes.CreateAsync(new CreateRecipeRequest("Chicken & rice", 2, 20, null, ["dinner"],
            [new RecipeIngredientInput(200, "g", "rice"), new RecipeIngredientInput(300, "g", "chicken")]), ownerUserId: userId);

        // Already have 100 g of rice on hand.
        db.PantryItems.Add(new PantryItem { UserId = userId, IngredientId = rice.Id, IngredientName = "rice", Grams = 100 });
        await db.SaveChangesAsync(CancellationToken.None);

        var shopping = new ShoppingListService(db, db);
        var list = await shopping.GenerateAsync(userId,
            [new ShoppingRecipeLine(bowl.Id, 2), new ShoppingRecipeLine(chickenRice.Id, 2)], mealPlanId: null);

        // rice consolidated 300 + 200 = 500, minus 100 pantry = 400 g, in the Pantry aisle.
        var pantryAisle = list.Aisles.Single(a => a.Aisle == "Pantry");
        var riceItem = pantryAisle.Items.Single(i => i.IngredientName == "rice");
        Assert.Equal(400, riceItem.Grams);

        // chicken 300 g, grouped under its own aisle.
        var meatAisle = list.Aisles.Single(a => a.Aisle == "Meat & Seafood");
        Assert.Equal(300, meatAisle.Items.Single().Grams);
    }

    [Fact]
    public async Task Shopping_list_buys_raw_purchase_weight_using_the_yield_factor()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var rice = SeedFoodAndIngredient(db, "rice", "Pantry", 130, 2.7, 0.3, 28.2);
        rice.YieldFactor = 2.8;
        rice.RecipeGramsAreRaw = false; // the recipe states cooked rice
        await db.SaveChangesAsync(CancellationToken.None);

        var recipes = new RecipeService(db, new FakeCurrentUser(userId));
        var bowl = await recipes.CreateAsync(new CreateRecipeRequest("Rice bowl", 1, 10, null, ["dinner"],
            [new RecipeIngredientInput(280, "g", "rice")]), ownerUserId: userId);

        var shopping = new ShoppingListService(db, db);
        var list = await shopping.GenerateAsync(userId, [new ShoppingRecipeLine(bowl.Id, 1)], mealPlanId: null);

        // 280 g cooked rice ⇒ buy 100 g raw (280 / 2.8). Shopping is in RAW purchase weight.
        var riceItem = list.Aisles.SelectMany(a => a.Items).Single(i => i.IngredientName == "rice");
        Assert.Equal(100, riceItem.Grams, 1);
    }

    [Theory]
    [InlineData(2, "tbsp", null, 30)]      // 2 tbsp × 15 ml × 1.0 density
    [InlineData(1, "cup", null, 240)]      // 1 cup = 240 ml × 1.0
    [InlineData(1, "oz", null, 28.35)]     // mass
    [InlineData(2, "egg-count", 50.0, 100)] // count unit via grams-per-count
    public void Unit_converter_resolves_common_units(double qty, string unit, double? gramsPerCount, double expected)
    {
        var u = unit == "egg-count" ? "count" : unit;
        var grams = UnitConverter.ToGrams(qty, u, densityGPerMl: null, gramsPerCount: gramsPerCount);
        Assert.NotNull(grams);
        Assert.Equal(expected, grams!.Value, precision: 2);
    }
}
