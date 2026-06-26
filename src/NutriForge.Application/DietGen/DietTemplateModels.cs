namespace NutriForge.Application.DietGen;

/// <summary>A saved diet-plan preset as returned to the client.</summary>
public sealed record DietTemplateDto(
    Guid Id, string Name, string? DietSlug, double? KcalTarget,
    int? MaxPrepMinutes, int? MealsPerDay, int HorizonDays, int BlockSize, string? Desire);

/// <summary>Create or update a saved diet-plan preset.</summary>
public sealed record UpsertDietTemplateRequest(
    string Name, string? DietSlug, double? KcalTarget,
    int? MaxPrepMinutes, int? MealsPerDay, int? HorizonDays, int? BlockSize, string? Desire);
