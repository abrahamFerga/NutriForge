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
    string? ThumbnailUrl = null,
    // INTENT only: a request to create this as a shared GLOBAL recipe. Honored solely for admins (the
    // endpoint is the trust boundary); ignored for everyone else, who always get a private recipe. The
    // owner itself is never bound from the body — it is server-derived from the principal.
    bool Global = false);

/// <summary>A request to import a recipe from a URL (YouTube/web) or pasted recipe text.</summary>
public sealed record ImportRecipeRequest(string? Url, string? Text);

/// <summary>Outcome of an ownership-guarded recipe write (maps to 200 / 404 / 403 / 409).</summary>
public enum RecipeWriteStatus
{
    Ok,
    /// <summary>Not visible to the caller (global ∪ own) — reported as 404 so private recipes stay hidden.</summary>
    NotFound,
    /// <summary>Visible (e.g. a global recipe) but the caller isn't allowed to mutate it — 403.</summary>
    Forbidden,
    /// <summary>The write would violate a uniqueness invariant (e.g. a global already exists for the video) — 409.</summary>
    Conflict,
}

public sealed record RecipeWriteResult(RecipeWriteStatus Status, RecipeDto? Recipe);

/// <summary>
/// An editable, un-persisted draft produced by an import. The user reviews/edits it and confirms by
/// posting a <see cref="CreateRecipeRequest"/> to the normal create endpoint (which owns the numbers).
/// </summary>
public sealed record ImportPreviewDto(
    RecipeDto Draft,
    bool ServingsAssumed,
    IReadOnlyList<string> Warnings);

/// <summary>Outcome of an import preview (maps to 200 / 503 / 422).</summary>
public enum ImportOutcome
{
    /// <summary>A draft was produced (deterministically from JSON-LD, or by the AI extractor).</summary>
    Ok,
    /// <summary>The only way to parse this input is the AI extractor, which isn't configured — 503.</summary>
    NeedsExtractor,
    /// <summary>Nothing recognizable to import (no structured data, no extractable text) — 422.</summary>
    NotFound,
}

public sealed record ImportPreviewResult(ImportOutcome Outcome, ImportPreviewDto? Preview);

public sealed record RecipeIngredientDto(
    string Name, double Quantity, string? Unit, double Grams, bool Resolved,
    double Kcal, double ProteinG, double FatG, double CarbG,
    // Raw (purchase/cook) vs cooked (plated) weight for this line (#85). Equal to Grams when the
    // ingredient has no yield factor. RawGrams is what shopping buys; CookedGrams is what's plated.
    double RawGrams = 0, double CookedGrams = 0);

public sealed record RecipeDto(
    Guid Id, string Name, int Servings, int TotalMinutes, string? Instructions,
    IReadOnlyList<string> Tags, bool IsNutritionComputed,
    double KcalPerServing, double ProteinPerServing, double FatPerServing, double CarbPerServing,
    IReadOnlyList<RecipeIngredientDto> Ingredients,
    // Ownership: IsGlobal = admin-curated/shared; IsMine = owned by the caller (so the SPA can badge it
    // and gate edit/delete). A recipe is never both.
    bool IsGlobal = true, bool IsMine = false,
    // Provenance (null for hand-authored/seeded). Lets the SPA embed the source video.
    string? SourceUrl = null, string? SourceType = null, string? SourceVideoId = null, string? ThumbnailUrl = null);

public sealed record RecipeSummary(
    Guid Id, string Name, int Servings, int TotalMinutes, IReadOnlyList<string> Tags,
    bool IsNutritionComputed, double KcalPerServing, double ProteinPerServing, double FatPerServing, double CarbPerServing,
    bool IsGlobal = true, bool IsMine = false);

/// <summary>A recipe's ingredient quantities scaled to a target number of servings.</summary>
public sealed record ScaledRecipeDto(
    Guid Id, string Name, int OriginalServings, double TargetServings,
    IReadOnlyList<ScaledIngredientDto> Ingredients, double KcalPerServing);

public sealed record ScaledIngredientDto(string Name, double Quantity, string? Unit, double Grams);

internal static class RecipeMappings
{
    public static RecipeDto ToDto(
        this Recipe r, Guid? currentUserId = null, IReadOnlyDictionary<Guid, Ingredient>? ingredientsById = null) => new(
        r.Id, r.Name, r.Servings, r.TotalMinutes, r.Instructions, r.Tags, r.IsNutritionComputed,
        r.KcalPerServing, r.ProteinPerServing, r.FatPerServing, r.CarbPerServing,
        r.Ingredients.Select(i =>
        {
            var ing = i.IngredientId is { } id && ingredientsById is not null && ingredientsById.TryGetValue(id, out var x) ? x : null;
            return new RecipeIngredientDto(
                i.IngredientName, i.Quantity, i.Unit, i.Grams, i.Resolved, i.Kcal, i.ProteinG, i.FatG, i.CarbG,
                ing?.RawWeight(i.Grams) ?? i.Grams, ing?.CookedWeight(i.Grams) ?? i.Grams);
        }).ToList(),
        r.IsGlobal, r.IsMine(currentUserId),
        r.SourceUrl, r.SourceType, r.SourceVideoId, r.ThumbnailUrl);

    public static RecipeSummary ToSummary(this Recipe r, Guid? currentUserId = null) => new(
        r.Id, r.Name, r.Servings, r.TotalMinutes, r.Tags, r.IsNutritionComputed,
        r.KcalPerServing, r.ProteinPerServing, r.FatPerServing, r.CarbPerServing,
        r.IsGlobal, r.IsMine(currentUserId));

    /// <summary>True when the recipe is owned by <paramref name="currentUserId"/> (a real, non-global owner).</summary>
    private static bool IsMine(this Recipe r, Guid? currentUserId) =>
        r.OwnerUserId is { } owner && currentUserId is { } me && owner == me;
}
