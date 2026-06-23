using NutriForge.Domain.Common;

namespace NutriForge.Domain.Diary;

/// <summary>
/// One logged food in a user's daily diary. The nutrition is snapshotted at log time
/// (ADR-0006) so editing or re-importing the underlying food never rewrites history —
/// past days are immutable. Owner-scoped and audited.
/// </summary>
public sealed class DiaryEntry : IUserOwned, IAuditable, ITimestamped
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }

    public DateOnly Date { get; set; }
    public MealSlot MealSlot { get; set; }

    /// <summary>Ordering within the meal slot.</summary>
    public int Sequence { get; set; }

    public Guid FoodId { get; set; }
    public string FoodName { get; set; } = string.Empty;
    public Guid? PortionId { get; set; }
    public string PortionName { get; set; } = string.Empty;

    /// <summary>Number of portions (or grams if logged by weight) — see <see cref="Grams"/>.</summary>
    public double Quantity { get; set; }
    public double Grams { get; set; }

    // Immutable log-time snapshot (denormalized — never recomputed from the live food).
    public double Kcal { get; set; }
    public double ProteinG { get; set; }
    public double FatG { get; set; }
    public double CarbG { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Macros Snapshot => new(Kcal, ProteinG, FatG, CarbG);

    public void SetSnapshot(Macros macros)
    {
        Kcal = macros.Kcal;
        ProteinG = macros.ProteinG;
        FatG = macros.FatG;
        CarbG = macros.CarbG;
    }
}
