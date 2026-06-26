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
    public bool IsConfigured => agent.IsConfigured;

    /// <summary>
    /// Generate up to <paramref name="count"/> recipes owned by <paramref name="ownerUserId"/> and persist
    /// them (with computed nutrition). Returns the created recipes; empty when no AI provider is configured.
    /// </summary>
    public async Task<IReadOnlyList<RecipeDto>> GenerateAsync(
        Guid ownerUserId, RecipeBrief brief, int count, CancellationToken ct = default)
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
            var req = new CreateRecipeRequest(
                Name: d.Name,
                Servings: d.Servings is > 0 ? d.Servings.Value : brief.Servings,
                TotalMinutes: d.TotalMinutes ?? 0,
                Instructions: d.Instructions,
                Tags: d.Tags,
                Ingredients: [.. d.Ingredients.Select(i => new RecipeIngredientInput(i.Quantity ?? 0, i.Unit, i.Name, i.RawText))],
                SourceType: "ai-generated");

            created.Add(await recipes.CreateAsync(req, ownerUserId, ct).ConfigureAwait(false));
        }

        return created;
    }
}
