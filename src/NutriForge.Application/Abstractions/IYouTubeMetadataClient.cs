using System.Text.RegularExpressions;

namespace NutriForge.Application.Abstractions;

/// <summary>Public metadata for a YouTube video (title, description if available, thumbnail, channel).</summary>
public sealed record YouTubeMetadata(string Title, string? Description, string? ThumbnailUrl, string? ChannelTitle);

/// <summary>
/// Fetches public YouTube video metadata. The description (where most cooking channels paste the
/// recipe) is only available via the Data API when a key is configured; oEmbed gives title+thumbnail
/// with no key. Faults degrade to null. The transcript is deliberately NOT here (ToS — see Epic 14 P3).
/// </summary>
public interface IYouTubeMetadataClient
{
    Task<YouTubeMetadata?> GetAsync(string videoId, CancellationToken ct = default);
}

/// <summary>Pure parsing of a YouTube URL into its canonical 11-char video id (used for embed + dedup).</summary>
public static partial class YouTubeUrl
{
    [GeneratedRegex(
        @"(?:youtu\.be/|youtube\.com/(?:watch\?(?:.*&)?v=|shorts/|embed/|v/|live/))([A-Za-z0-9_-]{11})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex VideoIdPattern();

    /// <summary>Returns the 11-char video id, or null when the URL isn't a recognizable YouTube link.</summary>
    public static string? ParseVideoId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var match = VideoIdPattern().Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static bool IsYouTube(string? url) => ParseVideoId(url) is not null;
}
