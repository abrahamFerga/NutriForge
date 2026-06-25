using NutriForge.Domain.Common;

namespace NutriForge.Domain.Recipes;

/// <summary>
/// A canonical ingredient — the de-dup anchor recipes resolve against (a canonical alias table, no
/// embeddings at v1). Carries the aisle for shopping-list grouping, an optional density for
/// volume→mass conversion, and a default catalog food used to compute nutrition.
/// </summary>
public sealed class Ingredient : IAuditable, ITimestamped
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public string CanonicalName { get; set; } = string.Empty;

    /// <summary>Supermarket aisle (Produce, Dairy, Meat, Pantry, …) for shopping-list grouping.</summary>
    public string AisleCategory { get; set; } = "Other";

    /// <summary>Lower-cased alternative names that resolve to this ingredient.</summary>
    public List<string> Aliases { get; set; } = [];

    /// <summary>g per ml, for volume→mass conversion (water ≈ 1.0). Null ⇒ assume 1.0.</summary>
    public double? DensityGPerMl { get; set; }

    /// <summary>Catalog food whose per-100g profile supplies this ingredient's nutrition.</summary>
    public Guid? DefaultFoodId { get; set; }

    /// <summary>Grams of one "count" unit (e.g. one egg ≈ 50 g) when the recipe says "2 eggs".</summary>
    public double? GramsPerCount { get; set; }

    /// <summary>
    /// Cooking yield: <c>cooked = raw × YieldFactor</c>. Rice ≈ 2.8 (absorbs water), dry legumes ≈ 2.5,
    /// chicken ≈ 0.75 (loses water). Default 1.0 — eaten as-is, no raw/cooked distinction.
    /// </summary>
    public double YieldFactor { get; set; } = 1.0;

    /// <summary>
    /// Whether a recipe's grams for this ingredient are stated as the RAW (cook/purchase) weight — e.g.
    /// raw chicken — or the COOKED (plated) weight — e.g. cooked rice. This picks the direction the yield
    /// factor converts (it usually mirrors the basis of the linked catalog food). Default true (raw),
    /// the common case for whole ingredients.
    /// </summary>
    public bool RecipeGramsAreRaw { get; set; } = true;

    private double SafeYield => YieldFactor <= 0 ? 1.0 : YieldFactor;

    /// <summary>
    /// The RAW purchase/cook weight for a recipe quantity — what the shopping list must buy. When the
    /// recipe states cooked grams, this divides out the yield (you buy less rice than the cooked volume).
    /// </summary>
    public double RawWeight(double recipeGrams) => RecipeGramsAreRaw ? recipeGrams : recipeGrams / SafeYield;

    /// <summary>The COOKED/plated weight for a recipe quantity — what ends up on the plate.</summary>
    public double CookedWeight(double recipeGrams) => RecipeGramsAreRaw ? recipeGrams * SafeYield : recipeGrams;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool Matches(string rawName)
    {
        var n = rawName.Trim().ToLowerInvariant();
        return CanonicalName.Equals(n, StringComparison.OrdinalIgnoreCase)
            || Aliases.Contains(n, StringComparer.OrdinalIgnoreCase)
            || n.Contains(CanonicalName, StringComparison.OrdinalIgnoreCase);
    }
}
