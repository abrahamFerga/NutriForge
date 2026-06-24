using NutriForge.Domain.Common;

namespace NutriForge.Domain.Planning;

/// <summary>Lifecycle of an async diet-plan generation (ADR-0008).</summary>
public enum PlanStatus
{
    Generating = 1,
    Ready = 2,
    Infeasible = 3,
    Accepted = 4,
}

/// <summary>
/// A generated meal plan (the flagship output). Created in <see cref="PlanStatus.Generating"/> and
/// finished asynchronously by the generation worker to Ready or Infeasible. Owner-scoped, audited.
/// </summary>
public sealed class MealPlan : IUserOwned, IAuditable, ITimestamped
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }

    public PlanStatus Status { get; set; } = PlanStatus.Generating;

    /// <summary>The original free-text desire (if any).</summary>
    public string? Desire { get; set; }

    /// <summary>The structured intent the plan was generated against, as JSON.</summary>
    public string IntentJson { get; set; } = "{}";

    public int HorizonDays { get; set; } = 7;
    public double TargetKcal { get; set; }

    /// <summary>
    /// Number of people this plan is cooked and shopped for (default 1). This is a pure DOWNSTREAM
    /// multiplier: it scales the consolidated shopping list and the displayed cook totals only. It
    /// MUST NEVER enter generation — the generator targets ONE eater's daily kcal/macros, so
    /// <see cref="TargetKcal"/>, every <see cref="PlanSlot.Servings"/>, and the <c>Achieved*</c>
    /// daily averages all stay strictly per-eater. The application layer clamps this to [1, 9].
    /// </summary>
    public int Eaters { get; set; } = 1;

    // Achieved daily-average nutrition once generated, PER EATER (computed, not LLM-sourced).
    public double AchievedKcal { get; set; }
    public double AchievedProteinG { get; set; }
    public double AchievedFatG { get; set; }
    public double AchievedCarbG { get; set; }

    /// <summary>Explanation (Ready) or the honest infeasible reason + suggested relaxations.</summary>
    public string? Message { get; set; }

    private readonly List<PlanSlot> _slots = [];
    public IReadOnlyCollection<PlanSlot> Slots => _slots;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public PlanSlot AddSlot(PlanSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        slot.MealPlanId = Id;
        _slots.Add(slot);
        return slot;
    }

    public void ClearSlots() => _slots.Clear();
}

/// <summary>One meal in a plan: a recipe at a given day + meal slot, with computed servings/macros.</summary>
public sealed class PlanSlot
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid MealPlanId { get; set; }

    public int Day { get; set; }
    public MealSlot MealSlot { get; set; }

    public Guid RecipeId { get; set; }
    public string RecipeName { get; set; } = string.Empty;
    public double Servings { get; set; } = 1;

    // Computed nutrition for this slot (recipe per-serving × servings).
    public double Kcal { get; set; }
    public double ProteinG { get; set; }
    public double FatG { get; set; }
    public double CarbG { get; set; }

    public Macros Macros => new(Kcal, ProteinG, FatG, CarbG);

    public void SetMacros(Macros macros)
    {
        Kcal = macros.Kcal;
        ProteinG = macros.ProteinG;
        FatG = macros.FatG;
        CarbG = macros.CarbG;
    }
}
