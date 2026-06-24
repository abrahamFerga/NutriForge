using NutriForge.Application.Abstractions;

namespace NutriForge.Application.Recipes;

/// <summary>
/// Turns a YouTube URL (or pasted recipe text) into an editable, un-persisted <see cref="ImportPreviewDto"/>.
/// The extractor recognizes names/quantities/steps; the existing <see cref="RecipeService"/> resolver owns
/// every nutrition number (ADR-0004). Nothing is saved here — the user confirms via the create endpoint.
/// </summary>
public sealed class RecipeImportService(IRecipeExtractor extractor, IYouTubeMetadataClient youtube, RecipeService recipes)
{
    public bool IsConfigured => extractor.IsConfigured;

    public async Task<ImportPreviewDto?> PreviewAsync(ImportRecipeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var text = request.Text;
        var sourceUrl = string.IsNullOrWhiteSpace(request.Url) ? null : request.Url!.Trim();
        var sourceType = sourceUrl is null ? "manual" : "web";
        string? videoId = null;
        string? thumbnail = null;
        string? metaTitle = null;

        if (YouTubeUrl.ParseVideoId(sourceUrl) is { } vid)
        {
            sourceType = "youtube";
            videoId = vid;
            var meta = await youtube.GetAsync(vid, ct).ConfigureAwait(false);
            metaTitle = meta?.Title;
            thumbnail = meta?.ThumbnailUrl;
            // Pasted text wins; otherwise use the video description (the Data API path).
            if (string.IsNullOrWhiteSpace(text))
            {
                text = meta?.Description;
            }
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            // Nothing extractable — no key/description and no pasted text. Caller surfaces a 422.
            return null;
        }

        var extracted = await extractor.ExtractAsync(text, ct).ConfigureAwait(false);
        if (extracted.Count == 0)
        {
            return null;
        }

        var r = extracted[0]; // v1: take the first recipe; a candidate picker is a later phase.
        var servingsAssumed = r.Servings is null or <= 0;

        var draftRequest = new CreateRecipeRequest(
            Name: string.IsNullOrWhiteSpace(r.Name) ? (metaTitle ?? "Imported recipe") : r.Name,
            Servings: r.Servings is > 0 ? r.Servings.Value : 1,
            TotalMinutes: r.TotalMinutes ?? 0,
            Instructions: r.Instructions,
            Tags: r.Tags,
            Ingredients: r.Ingredients
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

        return new ImportPreviewDto(draft, servingsAssumed, warnings);
    }
}
