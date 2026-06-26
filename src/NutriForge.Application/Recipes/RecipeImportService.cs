using NutriForge.Application.Abstractions;

namespace NutriForge.Application.Recipes;

/// <summary>
/// Turns a recipe URL (web or YouTube) or pasted text/HTML into an editable, un-persisted
/// <see cref="ImportPreviewDto"/>. The PREFERRED path is deterministic + keyless: fetch the page and
/// parse its schema.org/Recipe <c>application/ld+json</c> (#91). Only messy text without structured
/// data (a YouTube description, free-text paste) falls back to the AI extractor — and even then the
/// existing <see cref="RecipeService"/> resolver owns every nutrition number (ADR-0004). Nothing is
/// saved here; the user confirms via the create endpoint.
/// </summary>
public sealed class RecipeImportService(
    IRecipeExtractor extractor, IYouTubeMetadataClient youtube, IRecipeUrlFetcher fetcher, RecipeService recipes)
{
    /// <summary>The AI extractor is configured. The deterministic JSON-LD path works regardless.</summary>
    public bool IsConfigured => extractor.IsConfigured;

    public async Task<ImportPreviewResult> PreviewAsync(ImportRecipeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var text = request.Text;
        var sourceUrl = string.IsNullOrWhiteSpace(request.Url) ? null : request.Url!.Trim();
        var sourceType = sourceUrl is null ? "manual" : "web";
        string? videoId = null;
        string? thumbnail = null;
        string? metaTitle = null;

        ExtractedRecipe? recipe = null;

        if (YouTubeUrl.ParseVideoId(sourceUrl) is { } vid)
        {
            // YouTube: no Recipe JSON-LD on a watch page, so this stays an AI-text path (the description).
            sourceType = "youtube";
            videoId = vid;
            var meta = await youtube.GetAsync(vid, ct).ConfigureAwait(false);
            metaTitle = meta?.Title;
            thumbnail = meta?.ThumbnailUrl;
            if (string.IsNullOrWhiteSpace(text))
            {
                text = meta?.Description;
            }
        }
        else if (sourceUrl is not null)
        {
            // Web URL: fetch (SSRF-guarded) and parse JSON-LD deterministically — no AI, no key.
            var page = await fetcher.FetchAsync(sourceUrl, ct).ConfigureAwait(false);
            if (page is not null)
            {
                recipe = JsonLdRecipeParser.Parse(page.Html);
            }
        }

        // Pasted HTML (e.g. "view source" of a recipe page) also goes through the JSON-LD parser.
        if (recipe is null && LooksLikeHtml(text))
        {
            recipe = JsonLdRecipeParser.Parse(text);
            if (recipe is not null && sourceType == "manual")
            {
                sourceType = "web";
            }
        }

        var servingsAssumed = false;

        if (recipe is null)
        {
            // No structured data — the only remaining option is the AI extractor over free text.
            if (!extractor.IsConfigured)
            {
                return new ImportPreviewResult(ImportOutcome.NeedsExtractor, null);
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return new ImportPreviewResult(ImportOutcome.NotFound, null);
            }

            var extracted = await extractor.ExtractAsync(text, ct).ConfigureAwait(false);
            if (extracted.Count == 0)
            {
                return new ImportPreviewResult(ImportOutcome.NotFound, null);
            }

            recipe = extracted[0]; // v1: first recipe; a candidate picker is a later phase
        }

        servingsAssumed = recipe.Servings is null or <= 0;

        var draftRequest = new CreateRecipeRequest(
            Name: string.IsNullOrWhiteSpace(recipe.Name) ? (metaTitle ?? "Imported recipe") : recipe.Name,
            Servings: recipe.Servings is > 0 ? recipe.Servings.Value : 1,
            TotalMinutes: recipe.TotalMinutes ?? 0,
            Instructions: recipe.Instructions,
            Tags: recipe.Tags,
            Ingredients: recipe.Ingredients
                .Select(i => new RecipeIngredientInput(i.Quantity ?? 0, i.Unit, i.Name, i.RawText))
                .ToList(),
            SourceUrl: sourceUrl,
            SourceType: sourceType,
            SourceVideoId: videoId,
            ThumbnailUrl: thumbnail);

        var draft = await recipes.PreviewAsync(draftRequest, ct).ConfigureAwait(false);

        var warnings = new List<string>();
        var unresolved = draft.Ingredients.Where(i => !i.Resolved).Select(i => i.Name).ToList();
        if (unresolved.Count > 0)
        {
            warnings.Add($"Couldn't match {unresolved.Count} ingredient(s) to the food catalog (they show 0 kcal): {string.Join(", ", unresolved)}.");
        }

        if (servingsAssumed)
        {
            warnings.Add("Servings wasn't stated — assumed 1. Set it before saving; it scales every per-serving macro.");
        }

        return new ImportPreviewResult(ImportOutcome.Ok, new ImportPreviewDto(draft, servingsAssumed, warnings));
    }

    private static bool LooksLikeHtml(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && (text.Contains("application/ld+json", StringComparison.OrdinalIgnoreCase)
            || text.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || text.Contains("<script", StringComparison.OrdinalIgnoreCase));
}
