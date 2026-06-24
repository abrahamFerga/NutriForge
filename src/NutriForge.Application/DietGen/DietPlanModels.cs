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
    int HorizonDays);

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
    int? Eaters = null);

public sealed record PlanSlotDto(
    int Day, string MealSlot, Guid RecipeId, string RecipeName, double Servings,
    double Kcal, double ProteinG, double FatG, double CarbG);

public sealed record DietPlanDto(
    Guid Id, string Status, double TargetKcal,
    // AchievedKcal/ProteinG/FatG/CarbG are PER-EATER daily averages — the UI must NOT multiply them
    // by Eaters. Servings on each slot is also per-eater; Eaters scales cook totals + the shopping list.
    double AchievedKcal, double AchievedProteinG, double AchievedFatG, double AchievedCarbG,
    int Eaters, string? Message, IReadOnlyList<PlanSlotDto> Slots);

public sealed record AdherenceDto(
    DateOnly Date, double PlannedKcal, double LoggedKcal, double AdherencePct);
