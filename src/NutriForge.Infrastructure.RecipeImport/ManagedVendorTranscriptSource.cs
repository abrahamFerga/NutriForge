using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using NutriForge.Application.Recipes;

namespace NutriForge.Infrastructure.RecipeImport;

/// <summary>
/// Transcript source that delegates to a managed external vendor API (#93). The vendor is expected
/// to expose <c>GET {VendorUrl}/transcript?videoId={videoId}</c> returning
/// <c>{ "text": "...", "language": "en" }</c> (or 404 when unavailable).
/// </summary>
public sealed partial class ManagedVendorTranscriptSource(
    HttpClient httpClient,
    TranscriptEnrichmentOptions options,
    ILogger<ManagedVendorTranscriptSource> logger) : ITranscriptSource
{
    public string Name => "vendor";
    public bool IsEnabled => options.IsEnabled && !string.IsNullOrWhiteSpace(options.VendorUrl);

    public async Task<TranscriptResult?> FetchAsync(string videoId, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            return null;
        }

        try
        {
            var url = $"{options.VendorUrl!.TrimEnd('/')}/transcript?videoId={Uri.EscapeDataString(videoId)}";
            var response = await httpClient.GetAsync(url, ct).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            var dto = await response.Content
                .ReadFromJsonAsync<VendorResponse>(ct)
                .ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(dto?.Text) ? null : new TranscriptResult(dto.Text, dto.Language);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFetchFailed(logger, videoId, ex);
            return null;
        }
    }

    private sealed class VendorResponse
    {
        public string? Text { get; set; }
        public string? Language { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Managed vendor transcript fetch failed for video {VideoId}; returning null (best-effort).")]
    private static partial void LogFetchFailed(ILogger logger, string videoId, Exception ex);
}
