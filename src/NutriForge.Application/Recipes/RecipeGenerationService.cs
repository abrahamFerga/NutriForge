using NutriForge.Application.Abstractions;

namespace NutriForge.Application.Recipes;

/// <summary>Ask the agent to generate recipes for the caller. Maps onto <see cref="RecipeBrief"/>.</summary>
public sealed record GenerateRecipesRequest(
    int? Count, string? MealType, string? DietSlug, double? TargetKcal,
    int? MaxPrepMinutes, IReadOnlyList<string>? Exclude, string? Cuisine, int? Servings);

/// <summary>
/// One brief to generate, how many recipes to write for it, and tags to force onto each (e.g. the
/// canonical meal-type + diet tags the planner filters on). A batch of these is generated concurrently.
/// </summary>
public sealed record RecipeBatchRequest(RecipeBrief Brief, int Count, IReadOnlyList<string>? ExtraTags = null);

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
    public Task<IReadOnlyList<RecipeDto>> GenerateAsync(
        Guid ownerUserId, RecipeBrief brief, int count,
        IReadOnlyList<string>? extraTags = null, string sourceType = CatalogSource, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(brief);
        return GenerateBatchesAsync(ownerUserId, [new RecipeBatchRequest(brief, count, extraTags)], sourceType, ct);
    }

    /// <summary>
    /// Generate several briefs for ONE owner at once. The LLM calls run CONCURRENTLY (they are independent
    /// and stateless), then the drafts are persisted SEQUENTIALLY because <see cref="RecipeService"/> shares
    /// the request's DbContext, which is not thread-safe. Each batch's <c>ExtraTags</c> are forced onto its
    /// recipes so the planner can filter on them (e.g. the meal-type tag, #101). Returns every created recipe
    /// in batch order; empty when no AI provider is configured.
    /// </summary>
    public async Task<IReadOnlyList<RecipeDto>> GenerateBatchesAsync(
        Guid ownerUserId, IReadOnlyList<RecipeBatchRequest> batches,
        string sourceType = CatalogSource, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batches);
        if (!agent.IsConfigured || batches.Count == 0)
        {
            return [];
        }

        // 1. Fan out the slow LLM generation — independent network calls, safe to run in parallel.
        var draftsPerBatch = await Task.WhenAll(
            batches.Select(b => agent.GenerateAsync(b.Brief, b.Count, ct))).ConfigureAwait(false);

        // 2. Persist sequentially — the shared DbContext must not be written from multiple threads.
        var created = new List<RecipeDto>();
        for (var i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            foreach (var d in draftsPerBatch[i])
            {
                // extraTags force diet/meal tags the planner filters on (e.g. "breakfast", "high-protein"),
                // so an auto-generated recipe is guaranteed to qualify for the slot it was generated for (#101).
                var tags = batch.ExtraTags is { Count: > 0 }
                    ? d.Tags.Concat(batch.ExtraTags).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                    : d.Tags;

                var req = new CreateRecipeRequest(
                    Name: d.Name,
                    Servings: d.Servings is > 0 ? d.Servings.Value : batch.Brief.Servings,
                    TotalMinutes: d.TotalMinutes ?? 0,
                    Instructions: d.Instructions,
                    Tags: tags,
                    Ingredients: [.. d.Ingredients.Select(i => new RecipeIngredientInput(i.Quantity ?? 0, i.Unit, i.Name, i.RawText))],
                    SourceType: sourceType);

                created.Add(await recipes.CreateAsync(req, ownerUserId, ct).ConfigureAwait(false));
            }
        }

        return created;
    }
}
