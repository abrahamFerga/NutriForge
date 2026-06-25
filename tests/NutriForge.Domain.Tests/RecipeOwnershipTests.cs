using NutriForge.Domain.Recipes;

namespace NutriForge.Domain.Tests;

/// <summary>
/// Ownership rules for <see cref="Recipe"/>: a recipe is global until it is assigned a real owner, the
/// empty guid is never a valid owner (so a system/unauth context can't mint an invisible recipe), and
/// promotion clears the owner back to global.
/// </summary>
public class RecipeOwnershipTests
{
    [Fact]
    public void New_recipe_is_global_by_default()
    {
        var recipe = new Recipe { Name = "Bowl" };
        Assert.True(recipe.IsGlobal);
        Assert.Null(recipe.OwnerUserId);
    }

    [Fact]
    public void AssignOwner_makes_it_private_to_that_user()
    {
        var owner = Guid.NewGuid();
        var recipe = new Recipe { Name = "Bowl" };

        recipe.AssignOwner(owner);

        Assert.False(recipe.IsGlobal);
        Assert.Equal(owner, recipe.OwnerUserId);
    }

    [Fact]
    public void AssignOwner_rejects_the_empty_guid()
    {
        var recipe = new Recipe { Name = "Bowl" };
        Assert.Throws<ArgumentException>(() => recipe.AssignOwner(Guid.Empty));
        // Left untouched — still global, not a phantom owned-by-nobody recipe.
        Assert.True(recipe.IsGlobal);
    }

    [Fact]
    public void MakeGlobal_clears_the_owner()
    {
        var recipe = new Recipe { Name = "Bowl" };
        recipe.AssignOwner(Guid.NewGuid());

        recipe.MakeGlobal();

        Assert.True(recipe.IsGlobal);
        Assert.Null(recipe.OwnerUserId);
    }

    [Fact]
    public void ClearIngredients_drops_all_lines()
    {
        var recipe = new Recipe { Name = "Bowl" };
        recipe.AddIngredient(new RecipeIngredient { IngredientName = "egg" });
        Assert.Single(recipe.Ingredients);

        recipe.ClearIngredients();

        Assert.Empty(recipe.Ingredients);
    }
}
