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
