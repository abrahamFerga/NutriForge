using NutriForge.Domain.Common;
using NutriForge.Domain.Nutrition;

namespace NutriForge.Domain.Tests;

public class NutritionTargetsTests
{
    // The worked example from docs/algorithms/tdee-and-macros.md — the golden test every
    // downstream subsystem trusts: male, 30y, 80kg, 180cm, moderately active, moderate cut,
    // protein-anchored → { kcal 2345, P 176, F 72, Cb 248 }.
    [Fact]
    public void Compute_worked_example_matches_documented_tuple()
    {
        var inputs = new TargetInputs(
            Sex.Male, AgeYears: 30, WeightKg: 80, HeightCm: 180,
            ActivityLevel.ModeratelyActive, Goal.ModerateCut, MacroStrategy.ProteinAnchored);

        var target = NutritionTargets.Compute(inputs);

        Assert.Equal(2345, target.Kcal);
        Assert.Equal(176, target.ProteinG);
        Assert.Equal(72, target.FatG);
        Assert.Equal(248, target.CarbG);
    }

    [Fact]
    public void Bmr_uses_mifflin_st_jeor_when_no_body_fat()
    {
        var inputs = new TargetInputs(
            Sex.Male, 30, 80, 180, ActivityLevel.ModeratelyActive, Goal.Maintain, MacroStrategy.ProteinAnchored);

        Assert.Equal(1780, NutritionTargets.Bmr(inputs), precision: 6);
        Assert.Equal(2759, NutritionTargets.Tdee(inputs), precision: 6);
    }

    [Fact]
    public void Bmr_switches_to_katch_mcardle_when_body_fat_known()
    {
        var inputs = new TargetInputs(
            Sex.Male, 30, 80, 180, ActivityLevel.Sedentary, Goal.Maintain, MacroStrategy.ProteinAnchored,
            BodyFatPct: 15);

        // 370 + 21.6 * (80 * 0.85) = 370 + 21.6 * 68 = 1838.8
        Assert.Equal(1838.8, NutritionTargets.Bmr(inputs), precision: 4);
    }

    [Theory]
    [InlineData(Sex.Male, 1500)]
    [InlineData(Sex.Female, 1200)]
    public void Calorie_target_never_drops_below_safety_floor(Sex sex, double floor)
    {
        // Tiny, very-low-activity body on an aggressive cut would compute below the floor.
        var inputs = new TargetInputs(
            sex, AgeYears: 80, WeightKg: 40, HeightCm: 150,
            ActivityLevel.Sedentary, Goal.AggressiveCut, MacroStrategy.ProteinAnchored);

        Assert.Equal(floor, NutritionTargets.CalorieTarget(inputs));
    }

    [Fact]
    public void Macro_kcal_reconcile_to_calorie_target()
    {
        var inputs = new TargetInputs(
            Sex.Female, 28, 65, 168, ActivityLevel.Active, Goal.LeanBulk, MacroStrategy.ProteinAnchored);

        var t = NutritionTargets.Compute(inputs);
        var macroKcal = (t.ProteinG * Macros.ProteinKcalPerG)
            + (t.CarbG * Macros.CarbKcalPerG)
            + (t.FatG * Macros.FatKcalPerG);

        // Carbs absorb the rounding remainder, so macro kcal land within ~4 kcal of the target.
        Assert.True(Math.Abs(macroKcal - t.Kcal) <= 4, $"macro kcal {macroKcal} vs target {t.Kcal}");
    }
}
