using NutriForge.Domain.Common;

namespace NutriForge.Domain.Nutrition;

/// <summary>Body stats + goal needed to derive a daily target. Pure input — no entity, no I/O.</summary>
public sealed record TargetInputs(
    Sex Sex,
    int AgeYears,
    double WeightKg,
    double HeightCm,
    ActivityLevel Activity,
    Goal Goal,
    MacroStrategy Strategy,
    double? BodyFatPct = null);

/// <summary>The derived daily target plus the formula used to produce it (for traceability/UI).</summary>
public sealed record NutritionTarget(Macros Macros, string Formula)
{
    public double Kcal => Macros.Kcal;
    public double ProteinG => Macros.ProteinG;
    public double FatG => Macros.FatG;
    public double CarbG => Macros.CarbG;
}

/// <summary>
/// The deterministic BMR → TDEE → calorie target → macro split math. Pure arithmetic, fully
/// unit-testable, no ML and no LLM — every downstream subsystem trusts these numbers
/// (see docs/algorithms/tdee-and-macros.md; the worked example is a golden test).
/// </summary>
public static class NutritionTargets
{
    // Protein-anchored defaults (g per kg of bodyweight) — match the worked example.
    private const double ProteinPerKg = 2.2;
    private const double FatPerKg = 0.9;

    // AMDR midpoints for the percentage strategy.
    private const double AmdrProteinPct = 0.30;
    private const double AmdrFatPct = 0.30;

    /// <summary>Hard calorie safety floor — a generated target is never below this (kcal).</summary>
    public static double SafetyFloor(Sex sex) => sex == Sex.Male ? 1500 : 1200;

    public static double ActivityFactor(ActivityLevel level) => level switch
    {
        ActivityLevel.Sedentary => 1.2,
        ActivityLevel.LightlyActive => 1.375,
        ActivityLevel.ModeratelyActive => 1.55,
        ActivityLevel.Active => 1.725,
        ActivityLevel.VeryActive => 1.9,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown activity level"),
    };

    public static double GoalAdjustment(Goal goal) => goal switch
    {
        Goal.AggressiveCut => -0.20,
        Goal.ModerateCut => -0.15,
        Goal.Maintain => 0.0,
        Goal.LeanBulk => +0.10,
        Goal.Bulk => +0.20,
        _ => throw new ArgumentOutOfRangeException(nameof(goal), goal, "Unknown goal"),
    };

    /// <summary>
    /// Basal Metabolic Rate. Mifflin-St Jeor by default; Katch-McArdle when body-fat % is known
    /// (more accurate for lean/athletic users).
    /// </summary>
    public static double Bmr(TargetInputs i)
    {
        if (i.BodyFatPct is { } bf and >= 0 and < 100)
        {
            var leanMass = i.WeightKg * (1 - (bf / 100.0));
            return 370 + (21.6 * leanMass);
        }

        var s = i.Sex == Sex.Male ? 5 : -161;
        return (10 * i.WeightKg) + (6.25 * i.HeightCm) - (5 * i.AgeYears) + s;
    }

    /// <summary>Total Daily Energy Expenditure = BMR × activity factor (gross accounting).</summary>
    public static double Tdee(TargetInputs i) => Bmr(i) * ActivityFactor(i.Activity);

    /// <summary>Goal-adjusted calorie target, never below the per-sex safety floor.</summary>
    public static double CalorieTarget(TargetInputs i)
    {
        var adjusted = Tdee(i) * (1 + GoalAdjustment(i.Goal));
        return Math.Max(adjusted, SafetyFloor(i.Sex));
    }

    /// <summary>Derive the full daily target (calories + macro split), rounded to the user-facing tuple.</summary>
    public static NutritionTarget Compute(TargetInputs i)
    {
        ArgumentNullException.ThrowIfNull(i);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(i.WeightKg);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(i.HeightCm);

        var kcal = CalorieTarget(i);

        double proteinG, fatG;
        string formula;
        var bmrName = i.BodyFatPct is >= 0 and < 100 ? "Katch-McArdle" : "Mifflin-St Jeor";

        if (i.Strategy == MacroStrategy.ProteinAnchored)
        {
            proteinG = i.WeightKg * ProteinPerKg;
            fatG = i.WeightKg * FatPerKg;
            formula = $"{bmrName} → TDEE×{ActivityFactor(i.Activity):0.###} → goal {GoalAdjustment(i.Goal):+0%;-0%;0%} → protein-anchored {ProteinPerKg}g/kg P, {FatPerKg}g/kg F";
        }
        else
        {
            proteinG = kcal * AmdrProteinPct / Macros.ProteinKcalPerG;
            fatG = kcal * AmdrFatPct / Macros.FatKcalPerG;
            formula = $"{bmrName} → TDEE×{ActivityFactor(i.Activity):0.###} → goal {GoalAdjustment(i.Goal):+0%;-0%;0%} → AMDR {AmdrProteinPct:0%} P / {AmdrFatPct:0%} F";
        }

        // Round protein & fat first, then let carbs absorb the remainder so the macro kcal
        // reconcile to the (rounded) calorie target — exactly as the worked example does.
        var kcalR = Math.Round(kcal, MidpointRounding.AwayFromZero);
        var proteinR = Math.Round(proteinG, MidpointRounding.AwayFromZero);
        var fatR = Math.Round(fatG, MidpointRounding.AwayFromZero);
        var carbKcal = kcalR - (proteinR * Macros.ProteinKcalPerG) - (fatR * Macros.FatKcalPerG);
        var carbR = Math.Round(Math.Max(carbKcal, 0) / Macros.CarbKcalPerG, MidpointRounding.AwayFromZero);

        return new NutritionTarget(new Macros(kcalR, proteinR, fatR, carbR), formula);
    }
}
