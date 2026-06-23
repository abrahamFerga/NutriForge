using NutriForge.Domain.Common;

namespace NutriForge.Domain.Recipes;

/// <summary>
/// A schema.org-aligned recipe whose per-serving nutrition is <b>computed</b> from its resolved
/// ingredients (never copied from the source). Public-read catalog; audited on mutation.
/// </summary>
public sealed class Recipe : IAuditable, ITimestamped
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public string Name { get; set; } = string.Empty;
    public int Servings { get; set; } = 1;
    public int TotalMinutes { get; set; }
    public string? Instructions { get; set; }

    /// <summary>Free-form tags (cuisine, "vegan", "high-protein", proteins) for filtering/variety.</summary>
    public List<string> Tags { get; set; } = [];

    private readonly List<RecipeIngredient> _ingredients = [];
    public IReadOnlyCollection<RecipeIngredient> Ingredients => _ingredients;

    // Cached computed per-serving nutrition (set by RecomputeNutrition; queryable for FILTER).
    public double KcalPerServing { get; private set; }
    public double ProteinPerServing { get; private set; }
    public double FatPerServing { get; private set; }
    public double CarbPerServing { get; private set; }

    /// <summary>True only when every ingredient resolved — diet generation uses these recipes only.</summary>
    public bool IsNutritionComputed { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Macros PerServing => new(KcalPerServing, ProteinPerServing, FatPerServing, CarbPerServing);

    public RecipeIngredient AddIngredient(RecipeIngredient ingredient)
    {
        ArgumentNullException.ThrowIfNull(ingredient);
        ingredient.RecipeId = Id;
        _ingredients.Add(ingredient);
        return ingredient;
    }

    /// <summary>Recompute the cached per-serving nutrition from the (resolved) ingredient contributions.</summary>
    public void RecomputeNutrition()
    {
        var servings = Servings <= 0 ? 1 : Servings;
        var total = _ingredients.Aggregate(Macros.Zero, (acc, i) => acc + i.Contribution);
        var perServing = total.Scale(1.0 / servings).Rounded();

        KcalPerServing = perServing.Kcal;
        ProteinPerServing = perServing.ProteinG;
        FatPerServing = perServing.FatG;
        CarbPerServing = perServing.CarbG;
        IsNutritionComputed = _ingredients.Count > 0 && _ingredients.All(i => i.Resolved);
    }

    /// <summary>Per-serving macros scaled to a number of servings.</summary>
    public Macros MacrosFor(double servings) => PerServing.Scale(servings);
}
