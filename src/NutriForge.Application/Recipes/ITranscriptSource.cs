namespace NutriForge.Application.Recipes;

/// <summary>Fetched caption / transcript text for a YouTube video (language + cleaned text).</summary>
public sealed record TranscriptResult(string Text, string? Language);

/// <summary>
/// Best-effort transcript source for YouTube recipe videos (#93). Implementations are opt-in and
/// isolated to the ImportWorker — this interface is never called on the API request path.
/// Implementations must never throw; callers treat <c>null</c> as "not available".
/// </summary>
public interface ITranscriptSource
{
    /// <summary>Provider key, e.g. "none", "youtube-explode", "vendor".</summary>
    string Name { get; }

    /// <summary>False if the source is not configured or explicitly disabled.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Fetch the transcript for the given YouTube video ID. Returns <c>null</c> when unavailable
    /// (private video, no captions, provider error). Never throws.
    /// </summary>
    Task<TranscriptResult?> FetchAsync(string videoId, CancellationToken ct = default);
}
