using NutriForge.Domain.Common;
using NutriForge.Domain.Nutrition;

namespace NutriForge.Domain.Users;

/// <summary>
/// A user's body stats + dietary preferences. Health-adjacent personal data: the [Pii] fields
/// drive the GDPR export bundle and audit redaction. Owner-scoped (isolated by <see cref="UserId"/>).
/// </summary>
public sealed class Profile : IUserOwned, IAuditable, ITimestamped
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }

    [Pii] public Sex Sex { get; set; }
    [Pii] public DateOnly BirthDate { get; set; }
    [Pii] public double HeightCm { get; set; }
    [Pii] public double WeightKg { get; set; }
    [Pii] public double? BodyFatPct { get; set; }

    public ActivityLevel Activity { get; set; } = ActivityLevel.Sedentary;
    public Goal Goal { get; set; } = Goal.Maintain;
    public MacroStrategy MacroStrategy { get; set; } = MacroStrategy.ProteinAnchored;

    /// <summary>Declared allergens — safety-critical (hard-excluded, never merely preferred).</summary>
    public List<string> Allergens { get; set; } = [];
    public List<string> Dislikes { get; set; } = [];
    public List<string> PreferredDiets { get; set; } = [];

    /// <summary>Bumped on every change so the derived <see cref="Target"/> cache can be invalidated.</summary>
    public int Version { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public int AgeOn(DateOnly today)
    {
        var age = today.Year - BirthDate.Year;
        if (BirthDate > today.AddYears(-age))
        {
            age--;
        }

        return age;
    }

    /// <summary>Build the pure-math inputs for <see cref="NutritionTargets"/> as of <paramref name="today"/>.</summary>
    public TargetInputs ToTargetInputs(DateOnly today) =>
        new(Sex, AgeOn(today), WeightKg, HeightCm, Activity, Goal, MacroStrategy, BodyFatPct);
}
