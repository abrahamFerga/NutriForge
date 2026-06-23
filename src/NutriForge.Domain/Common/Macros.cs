namespace NutriForge.Domain.Common;

/// <summary>
/// An immutable energy + macronutrient tuple (the one value object every number-bearing
/// subsystem speaks). Calories are kcal; macros are grams. Atwater factors: P 4, C 4, F 9.
/// </summary>
public readonly record struct Macros(double Kcal, double ProteinG, double FatG, double CarbG)
{
    /// <summary>Atwater energy factors (kcal per gram).</summary>
    public const double ProteinKcalPerG = 4;
    public const double CarbKcalPerG = 4;
    public const double FatKcalPerG = 9;

    public static Macros Zero => new(0, 0, 0, 0);

    /// <summary>Energy implied by the macro grams (may differ from <see cref="Kcal"/> by rounding).</summary>
    public double KcalFromMacros => (ProteinG * ProteinKcalPerG) + (CarbG * CarbKcalPerG) + (FatG * FatKcalPerG);

    public static Macros operator +(Macros a, Macros b) =>
        new(a.Kcal + b.Kcal, a.ProteinG + b.ProteinG, a.FatG + b.FatG, a.CarbG + b.CarbG);

    /// <summary>Scale every component (e.g. grams of a food → its contribution).</summary>
    public Macros Scale(double factor) =>
        new(Kcal * factor, ProteinG * factor, FatG * factor, CarbG * factor);

    /// <summary>Round every component to whole numbers (the user-facing tuple).</summary>
    public Macros Rounded() => new(
        Math.Round(Kcal, MidpointRounding.AwayFromZero),
        Math.Round(ProteinG, MidpointRounding.AwayFromZero),
        Math.Round(FatG, MidpointRounding.AwayFromZero),
        Math.Round(CarbG, MidpointRounding.AwayFromZero));
}
