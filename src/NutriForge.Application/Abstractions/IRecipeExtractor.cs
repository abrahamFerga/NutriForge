namespace NutriForge.Application.Abstractions;

/// <summary>One ingredient line a recipe extraction produced, before catalog resolution.</summary>
public sealed record ExtractedIngredient(string RawText, string Name, double? Quantity, string? Unit);

/// <summary>A recipe an extractor recognized from text — names/quantities/steps only, never nutrition.</summary>
public sealed record ExtractedRecipe(
    string Name,
    int? Servings,
    int? TotalMinutes,
    string? Instructions,
    IReadOnlyList<string> Tags,
    IReadOnlyList<ExtractedIngredient> Ingredients);

/// <summary>
/// Extracts structured recipe(s) from messy text (a YouTube description, a transcript, or pasted
/// text). The LLM does ONLY recognition — names, quantities, steps — and NEVER emits nutrition; the
/// catalog resolver owns every macro (ADR-0004). Provider-swappable; unconfigured ⇒ 503.
/// </summary>
public interface IRecipeExtractor
{
    bool IsConfigured { get; }

    Task<IReadOnlyList<ExtractedRecipe>> ExtractAsync(string text, CancellationToken ct = default);
}
