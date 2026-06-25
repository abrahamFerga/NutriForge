using System.ComponentModel.DataAnnotations.Schema;
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
    /// Day-block rotation (cook-once-eat-N-days): the number of consecutive days each cooked meal-set
    /// covers. <c>1</c> (default) = a distinct meal-set every day (no rotation). A 6-day plan with
    /// <c>BlockSize=3</c> is two 3-day blocks — the user cooks two meal-sets and eats each for 3 days.
    /// The generator emits one distinct meal-set per block (each <see cref="PlanSlot.Day"/> is a BLOCK
    /// index, not a calendar day); shopping weights each block by its day-count. Like Eaters it is a pure
    /// downstream concern — it NEVER changes the per-day target/macros. Clamped to [1, HorizonDays].
    /// </summary>
    public int BlockSize { get; set; } = 1;

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

    private readonly List<PlanMember> _members = [];

    /// <summary>The people this plan is cooked for, each portion-scaled to their own target.</summary>
    public IReadOnlyCollection<PlanMember> Members => _members;

    /// <summary>
    /// The single downstream multiplier that scales the consolidated shopping list and displayed cook
    /// totals. With members, it is the SUM of their portion factors (everyone eats the same meals,
    /// portion-scaled to their own target); with no members it falls back to <see cref="Eaters"/>
    /// (identical portions). Like Eaters it NEVER enters generation — only shopping + display.
    /// </summary>
    [NotMapped]
    public double PortionMultiplier => _members.Count > 0
        ? _members.Sum(m => FactorOf(m.TargetKcal, TargetKcal))
        : Eaters;

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

    public PlanMember AddMember(PlanMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        member.MealPlanId = Id;
        _members.Add(member);
        return member;
    }

    public void ClearMembers() => _members.Clear();

    /// <summary>
    /// A member's portion factor = their daily kcal relative to the plan's (primary) target, clamped to
    /// a safety envelope so one member can't dominate or vanish the merged grocery total. Pure.
    /// </summary>
    public static double FactorOf(double memberKcal, double planTargetKcal) =>
        Math.Clamp(memberKcal / Math.Max(planTargetKcal, 1), 0.25, 3.0);

    /// <summary>The effective block size, clamped to a sane [1, HorizonDays].</summary>
    private int EffectiveBlockSize => Math.Clamp(BlockSize, 1, Math.Max(HorizonDays, 1));

    /// <summary>How many distinct meal-sets (blocks) the plan cooks: ⌈HorizonDays / BlockSize⌉.</summary>
    [NotMapped]
    public int NumBlocks => (int)Math.Ceiling(Math.Max(HorizonDays, 1) / (double)EffectiveBlockSize);

    /// <summary>
    /// The number of calendar days a block (1-indexed) covers — usually <see cref="BlockSize"/>, but the
    /// final block is shorter when the horizon isn't a whole multiple (e.g. 7 days / blockSize 3 ⇒ 3,3,1).
    /// Each block's groceries scale by this. Returns 0 for an out-of-range index.
    /// </summary>
    public int BlockDaysFor(int block)
    {
        if (block < 1 || block > NumBlocks)
        {
            return 0;
        }

        var size = EffectiveBlockSize;
        return Math.Min(block * size, HorizonDays) - ((block - 1) * size);
    }
}

/// <summary>
/// A person eating a plan. Everyone eats the same meals; a member's portion of each meal scales by
/// <see cref="MealPlan.FactorOf"/>(<see cref="TargetKcal"/>, plan target). Pure data — no behaviour.
/// </summary>
public sealed class PlanMember
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid MealPlanId { get; set; }

    /// <summary>Display order; the owner is always 0. (Version-7 GUIDs aren't monotonic within a ms.)</summary>
    public int Sequence { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>The member's resolved per-day kcal target (the owner's equals the plan target).</summary>
    public double TargetKcal { get; set; }
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
