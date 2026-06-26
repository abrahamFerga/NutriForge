using NutriForge.Application.Abstractions;

namespace NutriForge.Application.Recipes;

/// <summary>Ask the agent to generate recipes for the caller. Maps onto <see cref="RecipeBrief"/>.</summary>
public sealed record GenerateRecipesRequest(
    int? Count, string? MealType, string? DietSlug, double? TargetKcal,
    int? MaxPrepMinutes, IReadOnlyList<string>? Exclude, string? Cuisine, int? Servings);

/// <summary>
/// Generates original recipes for a user (#101): the agent writes the recipe structure, then each one is
/// built + persisted through <see cref="RecipeService"/> — which resolves nutrition deterministically
/// (catalog ∪ the curated reference). So the user gets ready-to-use, owned recipes with real per-serving
/// macros without authoring anything. Owner-scoped; the created recipes flow into diet generation.
/// </summary>
public sealed class RecipeGenerationService(RecipeService recipes, IRecipeGenAgent agent)
{
    /// <summary>SourceType for recipes the user adds to their catalog via "Generate with AI" on Recipes.</summary>
    public const string CatalogSource = "ai-generated";

    /// <summary>
    /// SourceType for recipes the agent writes FOR a single diet plan. These are owned by the user (so the
    /// plan can reference them) but hidden from the Recipes catalog list, so generating a plan never
    /// pollutes the recipe browser.
    /// </summary>
    public const string PlanGeneratedSource = "plan-generated";

    public bool IsConfigured => agent.IsConfigured;

    /// <summary>
    /// Generate up to <paramref name="count"/> recipes owned by <paramref name="ownerUserId"/> and persist
    /// them (with computed nutrition). Returns the created recipes; empty when no AI provider is configured.
    /// </summary>
    public async Task<IReadOnlyList<RecipeDto>> GenerateAsync(
        Guid ownerUserId, RecipeBrief brief, int count,
        IReadOnlyList<string>? extraTags = null, string sourceType = CatalogSource, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(brief);
        if (!agent.IsConfigured)
        {
            return [];
        }

        var drafts = await agent.GenerateAsync(brief, count, ct).ConfigureAwait(false);
        var created = new List<RecipeDto>(drafts.Count);

        foreach (var d in drafts)
        {
            // extraTags force diet/meal tags the planner filters on (e.g. "high-protein"), so an
            // auto-generated recipe is guaranteed to qualify for the diet it was generated for (#101).
            var tags = extraTags is { Count: > 0 }
                ? d.Tags.Concat(extraTags).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : d.Tags;

            var req = new CreateRecipeRequest(
                Name: d.Name,
                Servings: d.Servings is > 0 ? d.Servings.Value : brief.Servings,
                TotalMinutes: d.TotalMinutes ?? 0,
                Instructions: d.Instructions,
                Tags: tags,
                Ingredients: [.. d.Ingredients.Select(i => new RecipeIngredientInput(i.Quantity ?? 0, i.Unit, i.Name, i.RawText))],
                SourceType: sourceType);

            created.Add(await recipes.CreateAsync(req, ownerUserId, ct).ConfigureAwait(false));
        }

        return created;
    }
}
