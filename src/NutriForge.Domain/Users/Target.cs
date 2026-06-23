using NutriForge.Domain.Common;
using NutriForge.Domain.Nutrition;

namespace NutriForge.Domain.Users;

/// <summary>
/// A user's derived (cached) daily calorie + macro target. Recomputed from the profile via
/// <see cref="NutritionTargets"/> on every profile change; <see cref="ProfileVersion"/> ties it
/// to the profile snapshot that produced it (cache invalidation key).
/// </summary>
public sealed class Target : IUserOwned, ITimestamped
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }

    public double Kcal { get; set; }
    public double ProteinG { get; set; }
    public double FatG { get; set; }
    public double CarbG { get; set; }

    public string Formula { get; set; } = string.Empty;
    public int ProfileVersion { get; set; }
    public DateTimeOffset ComputedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Macros Macros => new(Kcal, ProteinG, FatG, CarbG);

    public void ApplyFrom(NutritionTarget computed, int profileVersion, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(computed);
        Kcal = computed.Kcal;
        ProteinG = computed.ProteinG;
        FatG = computed.FatG;
        CarbG = computed.CarbG;
        Formula = computed.Formula;
        ProfileVersion = profileVersion;
        ComputedAt = now;
    }
}
