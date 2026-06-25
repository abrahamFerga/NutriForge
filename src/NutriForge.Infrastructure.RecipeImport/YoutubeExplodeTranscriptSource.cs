using Microsoft.Extensions.Logging;
using NutriForge.Application.Recipes;
using YoutubeExplode;
using YoutubeExplode.Videos.ClosedCaptions;

namespace NutriForge.Infrastructure.RecipeImport;

/// <summary>
/// Self-hosted transcript source using YoutubeExplode (#93). Best-effort and fragile: YouTube
/// introduced Proof-of-Origin Token (POT) authentication for caption requests in 2025, which
/// datacenter IPs cannot satisfy. This implementation will silently return null for most videos
/// in production; it is retained for local / residential-IP deployments where it may still work.
/// See ADR-0015 for the ToS considerations and the per-deployment opt-in requirement.
/// </summary>
public sealed partial class YoutubeExplodeTranscriptSource(
    ILogger<YoutubeExplodeTranscriptSource> logger) : ITranscriptSource
{
    private static readonly YoutubeClient Client = new();

    public string Name => "youtube-explode";
    public bool IsEnabled => true;

    public async Task<TranscriptResult?> FetchAsync(string videoId, CancellationToken ct = default)
    {
        try
        {
            var manifest = await Client.Videos.ClosedCaptions
                .GetManifestAsync(videoId, ct)
                .ConfigureAwait(false);

            var track = manifest.TryGetByLanguage("en")
                ?? (manifest.Tracks.Count > 0 ? manifest.Tracks[0] : null);

            if (track is null)
            {
                return null;
            }

            var cc = await Client.Videos.ClosedCaptions
                .GetAsync(track, ct)
                .ConfigureAwait(false);

            var text = string.Join(" ",
                cc.Captions
                    .Select(c => c.Text.Trim())
                    .Where(t => !string.IsNullOrEmpty(t)));

            return string.IsNullOrWhiteSpace(text) ? null : new TranscriptResult(text, track.Language.Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFetchFailed(logger, videoId, ex);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "YoutubeExplode transcript fetch failed for video {VideoId}; returning null (best-effort).")]
    private static partial void LogFetchFailed(ILogger logger, string videoId, Exception ex);
}
