using NutriForge.Domain.Recipes;

namespace NutriForge.Domain.Tests;

/// <summary>
/// Raw↔cooked yield conversion (#85): cooked = raw × YieldFactor. The conversion direction depends on
/// whether a recipe states the ingredient in raw (cook/purchase) or cooked (plated) grams — shopping
/// always wants raw, the plate always wants cooked.
/// </summary>
public class IngredientYieldTests
{
    [Fact]
    public void Cooked_basis_ingredient_buys_raw_below_the_cooked_weight()
    {
        // Recipe states cooked rice; you buy the smaller raw weight (rice ~triples cooking).
        var rice = new Ingredient { CanonicalName = "rice", YieldFactor = 2.8, RecipeGramsAreRaw = false };
        Assert.Equal(100, rice.RawWeight(280), 3);    // 280 g cooked ⇐ 100 g raw
        Assert.Equal(280, rice.CookedWeight(280), 3); // grams already cooked
    }

    [Fact]
    public void Raw_basis_ingredient_cooks_down_from_the_stated_raw_weight()
    {
        // Recipe states raw chicken; you buy it as-is, and it loses water on the plate.
        var chicken = new Ingredient { CanonicalName = "chicken", YieldFactor = 0.75, RecipeGramsAreRaw = true };
        Assert.Equal(300, chicken.RawWeight(300), 3);
        Assert.Equal(225, chicken.CookedWeight(300), 3);
    }

    [Fact]
    public void Default_yield_is_the_identity()
    {
        var tofu = new Ingredient { CanonicalName = "tofu" }; // YieldFactor 1.0, raw basis
        Assert.Equal(200, tofu.RawWeight(200), 3);
        Assert.Equal(200, tofu.CookedWeight(200), 3);
    }

    [Fact]
    public void Non_positive_yield_is_treated_as_one()
    {
        var ing = new Ingredient { YieldFactor = 0, RecipeGramsAreRaw = false };
        Assert.Equal(150, ing.RawWeight(150), 3);    // no divide-by-zero
        Assert.Equal(150, ing.CookedWeight(150), 3);
    }
}
