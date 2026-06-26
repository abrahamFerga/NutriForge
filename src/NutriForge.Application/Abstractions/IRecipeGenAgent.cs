namespace NutriForge.Application.Abstractions;

/// <summary>
/// What to generate recipes for. All fields are soft guidance to the model EXCEPT <see cref="Exclude"/>,
/// which carries safety-critical allergens (and dislikes) the generated recipes must avoid — that is
/// re-checked deterministically downstream, never trusted from the model.
/// </summary>
public sealed record RecipeBrief(
    string? MealType,
    string? DietSlug,
    double? TargetKcal,
    int? MaxPrepMinutes,
    IReadOnlyList<string>? Exclude,
    string? Cuisine,
    int Servings = 4);

/// <summary>
/// Generates full, original recipes (title, servings, steps, ingredient lines) from a brief — so the
/// user never has to author recipes by hand (#101). Like the extractor, the LLM does ONLY structure and
/// recognition: it names ingredients and writes steps but NEVER emits a nutrition number; the catalog +
/// reference resolver own every macro (ADR-0004). The model is instructed to prefer common ingredients so
/// resolution succeeds. Provider-swappable; unconfigured ⇒ the feature reports 503.
/// </summary>
public interface IRecipeGenAgent
{
    bool IsConfigured { get; }

    /// <summary>Generate up to <paramref name="count"/> distinct recipes matching the brief.</summary>
    Task<IReadOnlyList<ExtractedRecipe>> GenerateAsync(RecipeBrief brief, int count, CancellationToken ct = default);
}
