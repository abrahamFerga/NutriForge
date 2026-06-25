using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NutriForge.Application.Abstractions;

namespace NutriForge.Infrastructure.RecipeImport;

/// <summary>API key for the YouTube Data API (optional). Without it, only oEmbed (title+thumbnail) works.</summary>
public sealed class YouTubeOptions
{
    public string? ApiKey { get; init; }
}

/// <summary>
/// Fetches public YouTube metadata: the Data API <c>videos.list</c> (title + DESCRIPTION) when a key is
/// configured, falling back to keyless oEmbed (title + thumbnail, no description). Outbound, behind the
/// shared resilience handler; faults degrade to null rather than throwing.
/// </summary>
public sealed partial class YouTubeMetadataClient(HttpClient http, YouTubeOptions options, ILogger<YouTubeMetadataClient> logger)
    : IYouTubeMetadataClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<YouTubeMetadata?> GetAsync(string videoId, CancellationToken ct = default)
    {
        var id = (videoId ?? string.Empty).Trim();
        if (id.Length == 0)
        {
            return null;
        }

        // Preferred: Data API gives the description (where the recipe usually lives).
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            try
            {
                var url = $"https://www.googleapis.com/youtube/v3/videos?part=snippet&id={Uri.EscapeDataString(id)}&key={Uri.EscapeDataString(options.ApiKey)}";
                var resp = await http.GetFromJsonAsync<DataApiResponse>(url, Json, ct).ConfigureAwait(false);
                var snippet = resp?.Items is { Count: > 0 } items ? items[0].Snippet : null;
                if (snippet is not null)
                {
                    return new YouTubeMetadata(
                        snippet.Title ?? "",
                        snippet.Description,
                        snippet.Thumbnails?.High?.Url ?? snippet.Thumbnails?.Medium?.Url ?? snippet.Thumbnails?.Default?.Url,
                        snippet.ChannelTitle);
                }
            }
#pragma warning disable CA1031 // outbound fetch is best-effort; never fail the request
            catch (Exception ex)
            {
                LogFetchFailed(logger, id, ex);
            }
#pragma warning restore CA1031
        }

        // Fallback: keyless oEmbed (title + thumbnail + author, no description).
        try
        {
            var oembed = $"https://www.youtube.com/oembed?url=https://www.youtube.com/watch?v={Uri.EscapeDataString(id)}&format=json";
            var resp = await http.GetFromJsonAsync<OEmbedResponse>(oembed, Json, ct).ConfigureAwait(false);
            if (resp is not null && !string.IsNullOrWhiteSpace(resp.Title))
            {
                return new YouTubeMetadata(resp.Title!, null, resp.ThumbnailUrl, resp.AuthorName);
            }
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            LogFetchFailed(logger, id, ex);
        }
#pragma warning restore CA1031

        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "YouTube metadata fetch failed for {VideoId}")]
    private static partial void LogFetchFailed(ILogger logger, string videoId, Exception ex);

    // ---- wire models ----
    internal sealed class DataApiResponse
    {
        public List<DataApiItem>? Items { get; set; }
    }

    internal sealed class DataApiItem
    {
        public DataApiSnippet? Snippet { get; set; }
    }

    internal sealed class DataApiSnippet
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? ChannelTitle { get; set; }
        public Thumbnails? Thumbnails { get; set; }
    }

    internal sealed class Thumbnails
    {
        public Thumbnail? Default { get; set; }
        public Thumbnail? Medium { get; set; }
        public Thumbnail? High { get; set; }
    }

    internal sealed class Thumbnail
    {
        public string? Url { get; set; }
    }

    internal sealed class OEmbedResponse
    {
        public string? Title { get; set; }

        [JsonPropertyName("thumbnail_url")]
        public string? ThumbnailUrl { get; set; }

        [JsonPropertyName("author_name")]
        public string? AuthorName { get; set; }
    }
}
