using NutriForge.Domain.Common;

namespace NutriForge.Domain.Recipes;

/// <summary>
/// One ingredient line of a recipe, resolved to a canonical ingredient + grams, with its computed
/// nutrition contribution. The macros are computed from the resolved food (never trusted from the
/// recipe source) — that is the differentiator.
/// </summary>
public sealed class RecipeIngredient
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RecipeId { get; set; }

    public string RawText { get; set; } = string.Empty;
    public double Quantity { get; set; }
    public string? Unit { get; set; }

    public Guid? IngredientId { get; set; }
    public string IngredientName { get; set; } = string.Empty;

    /// <summary>Resolved mass; 0 when the line couldn't be resolved deterministically.</summary>
    public double Grams { get; set; }

    /// <summary>True once the line is resolved to an ingredient + grams + nutrition.</summary>
    public bool Resolved { get; set; }

    // Computed nutrition contribution for this line's grams (from the resolved food's per-100g).
    public double Kcal { get; set; }
    public double ProteinG { get; set; }
    public double FatG { get; set; }
    public double CarbG { get; set; }

    public Macros Contribution => new(Kcal, ProteinG, FatG, CarbG);

    public void SetContribution(double grams, Macros macros)
    {
        Grams = grams;
        Kcal = macros.Kcal;
        ProteinG = macros.ProteinG;
        FatG = macros.FatG;
        CarbG = macros.CarbG;
        Resolved = true;
    }
}
