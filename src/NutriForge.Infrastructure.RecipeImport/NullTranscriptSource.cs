using NutriForge.Application.Recipes;

namespace NutriForge.Infrastructure.RecipeImport;

/// <summary>No-op transcript source registered when <c>TranscriptEnrichment:Provider = "none"</c> (the default).</summary>
internal sealed class NullTranscriptSource : ITranscriptSource
{
    public string Name => "none";
    public bool IsEnabled => false;

    public Task<TranscriptResult?> FetchAsync(string videoId, CancellationToken ct = default) =>
        Task.FromResult<TranscriptResult?>(null);
}
