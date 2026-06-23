using NutriForge.Domain.Common;

namespace NutriForge.Domain.Catalog;

/// <summary>
/// Per-100g nutrient vector for a <see cref="Food"/>. Macros are required; micros are nullable
/// (not every source carries them). Owned 1:1 by its food.
/// </summary>
public sealed class NutrientProfile
{
    public Guid FoodId { get; set; }

    // Macros (required) — per 100 g.
    public double KcalPer100g { get; set; }
    public double ProteinPer100g { get; set; }
    public double FatPer100g { get; set; }
    public double CarbPer100g { get; set; }

    // Micros (nullable) — per 100 g.
    public double? FiberPer100g { get; set; }
    public double? SugarPer100g { get; set; }
    public double? SodiumMgPer100g { get; set; }

    /// <summary>The macro tuple for 100 g of this food.</summary>
    public Macros Per100g => new(KcalPer100g, ProteinPer100g, FatPer100g, CarbPer100g);
}
