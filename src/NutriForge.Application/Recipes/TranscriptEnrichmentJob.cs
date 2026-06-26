using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;

namespace NutriForge.Application.Recipes;

/// <summary>
/// Background job (ImportWorker) that enriches YouTube recipes with their closed-caption transcript.
/// Processes up to <see cref="BatchSize"/> un-transcripted recipes per run; sets
/// <see cref="Domain.Recipes.Recipe.TranscriptFetchedAt"/> even on null results so failures are not
/// retried every run.
/// </summary>
public sealed class TranscriptEnrichmentJob(
    ICatalogDbContext db,
    ITranscriptSource source)
{
    private const int BatchSize = 20;

    public bool IsEnabled => source.IsEnabled;

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        if (!source.IsEnabled)
        {
            return 0;
        }

        var recipes = await db.Recipes
            .IgnoreQueryFilters()
            .Where(r => r.SourceVideoId != null && r.TranscriptFetchedAt == null)
            .OrderBy(r => r.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (recipes.Count == 0)
        {
            return 0;
        }

        var enriched = 0;
        var stamp = DateTimeOffset.UtcNow;
        foreach (var recipe in recipes)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            var result = await source.FetchAsync(recipe.SourceVideoId!, ct).ConfigureAwait(false);
            recipe.TranscriptText = result?.Text;
            recipe.TranscriptFetchedAt = stamp;
            enriched++;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return enriched;
    }
}
