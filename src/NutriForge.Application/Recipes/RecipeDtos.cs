using NutriForge.Domain.Recipes;

namespace NutriForge.Application.Recipes;

public sealed record RecipeIngredientInput(double Quantity, string? Unit, string Name, string? RawText = null);

public sealed record CreateRecipeRequest(
    string Name,
    int Servings,
    int TotalMinutes,
    string? Instructions,
    IReadOnlyList<string>? Tags,
    IReadOnlyList<RecipeIngredientInput> Ingredients,
    // Optional provenance, set when the recipe is imported from a URL/video. Additive.
    string? SourceUrl = null,
    string? SourceType = null,
    string? SourceVideoId = null,
    string? ThumbnailUrl = null);

/// <summary>A request to import a recipe from a URL (YouTube/web) or pasted recipe text.</summary>
public sealed record ImportRecipeRequest(string? Url, string? Text);

/// <summary>
/// An editable, un-persisted draft produced by an import. The user reviews/edits it and confirms by
/// posting a <see cref="CreateRecipeRequest"/> to the normal create endpoint (which owns the numbers).
/// </summary>
public sealed record ImportPreviewDto(
    RecipeDto Draft,
    bool ServingsAssumed,
    IReadOnlyList<string> Warnings);

public sealed record RecipeIngredientDto(
    string Name, double Quantity, string? Unit, double Grams, bool Resolved,
    double Kcal, double ProteinG, double FatG, double CarbG);

public sealed record RecipeDto(
    Guid Id, string Name, int Servings, int TotalMinutes, string? Instructions,
    IReadOnlyList<string> Tags, bool IsNutritionComputed,
    double KcalPerServing, double ProteinPerServing, double FatPerServing, double CarbPerServing,
    IReadOnlyList<RecipeIngredientDto> Ingredients,
    // Provenance (null for hand-authored/seeded). Lets the SPA embed the source video.
    string? SourceUrl = null, string? SourceType = null, string? SourceVideoId = null, string? ThumbnailUrl = null);

public sealed record RecipeSummary(
    Guid Id, string Name, int Servings, int TotalMinutes, IReadOnlyList<string> Tags,
    bool IsNutritionComputed, double KcalPerServing, double ProteinPerServing, double FatPerServing, double CarbPerServing);

/// <summary>A recipe's ingredient quantities scaled to a target number of servings.</summary>
public sealed record ScaledRecipeDto(
    Guid Id, string Name, int OriginalServings, double TargetServings,
    IReadOnlyList<ScaledIngredientDto> Ingredients, double KcalPerServing);

public sealed record ScaledIngredientDto(string Name, double Quantity, string? Unit, double Grams);

internal static class RecipeMappings
{
    public static RecipeDto ToDto(this Recipe r) => new(
        r.Id, r.Name, r.Servings, r.TotalMinutes, r.Instructions, r.Tags, r.IsNutritionComputed,
        r.KcalPerServing, r.ProteinPerServing, r.FatPerServing, r.CarbPerServing,
        r.Ingredients.Select(i => new RecipeIngredientDto(
            i.IngredientName, i.Quantity, i.Unit, i.Grams, i.Resolved, i.Kcal, i.ProteinG, i.FatG, i.CarbG)).ToList(),
        r.SourceUrl, r.SourceType, r.SourceVideoId, r.ThumbnailUrl);

    public static RecipeSummary ToSummary(this Recipe r) => new(
        r.Id, r.Name, r.Servings, r.TotalMinutes, r.Tags, r.IsNutritionComputed,
        r.KcalPerServing, r.ProteinPerServing, r.FatPerServing, r.CarbPerServing);
}
