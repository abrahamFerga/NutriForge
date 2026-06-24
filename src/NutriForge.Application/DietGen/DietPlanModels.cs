namespace NutriForge.Application.DietGen;

/// <summary>
/// The structured intent a diet plan is generated against. Free-text desires (when an LLM is
/// configured) are parsed into this; otherwise the caller supplies it directly. The LLM never owns
/// these numbers downstream — FILTER/VERIFY/REPAIR are deterministic.
/// </summary>
public sealed record DietIntent(
    double? KcalTarget,
    double? ProteinTarget,
    string? DietSlug,
    IReadOnlyList<string> ExcludeKeywords,
    int? MaxPrepMinutes,
    int MealsPerDay,
    int HorizonDays,
    // Day-block rotation: how many days each cooked meal-set covers. 1 = distinct meals every day.
    // The generator emits one meal-set per ⌈HorizonDays/BlockSize⌉ block. Never changes the per-day target.
    int BlockSize = 1);

/// <summary>
/// One additional person to cook for (the owner is always included automatically as the primary).
/// A null <see cref="TargetKcal"/> means "same as me" ⇒ the plan's target ⇒ a portion factor of 1.0.
/// </summary>
public sealed record PlanMemberInput(string Name, double? TargetKcal);

public sealed record CreateDietPlanRequest(
    string? Desire,
    double? KcalTarget,
    string? DietSlug,
    IReadOnlyList<string>? ExcludeAllergens,
    IReadOnlyList<string>? Dislikes,
    int? MaxPrepMinutes,
    int? MealsPerDay,
    int? HorizonDays,
    // How many people to cook/shop for (default 1). A downstream multiplier only — it never enters
    // generation, so the plan is still built against ONE eater's target. Clamped to [1, 9].
    int? Eaters = null,
    // Day-block rotation (cook-once-eat-N-days): days each cooked meal-set covers. 1 (default) = a fresh
    // meal-set every day; e.g. a 6-day plan with BlockSize 3 cooks two sets. Clamped to [1, HorizonDays].
    int? BlockSize = null,
    // The OTHER people eating this plan (the owner is added automatically). When non-empty, this is
    // the sole quantity authority (Eaters is ignored): each eats the same meals, portion-scaled to
    // their own target. Still never enters generation.
    IReadOnlyList<PlanMemberInput>? Members = null);

public sealed record PlanSlotDto(
    int Day, string MealSlot, Guid RecipeId, string RecipeName, double Servings,
    double Kcal, double ProteinG, double FatG, double CarbG);

/// <summary>A person on the plan with their already-clamped portion factor (so the UI never re-derives it).</summary>
public sealed record PlanMemberDto(string Name, double TargetKcal, double PortionFactor);

public sealed record DietPlanDto(
    Guid Id, string Status, double TargetKcal,
    // AchievedKcal/ProteinG/FatG/CarbG are PER-PRIMARY daily averages — the UI must NOT multiply them
    // by the headcount. Servings on each slot is also per-primary. PortionMultiplier (= Σ member factors,
    // or Eaters when no members) scales cook totals + the shopping list; each member's portion = ×factor.
    double AchievedKcal, double AchievedProteinG, double AchievedFatG, double AchievedCarbG,
    int Eaters, double PortionMultiplier, IReadOnlyList<PlanMemberDto> Members,
    // Day-block rotation: HorizonDays total days cooked as NumBlocks meal-sets of BlockSize days each
    // (the last may be shorter). Each PlanSlotDto.Day is a BLOCK index (1..NumBlocks), not a calendar day.
    int HorizonDays, int BlockSize, int NumBlocks,
    string? Message, IReadOnlyList<PlanSlotDto> Slots);

public sealed record AdherenceDto(
    DateOnly Date, double PlannedKcal, double LoggedKcal, double AdherencePct);
