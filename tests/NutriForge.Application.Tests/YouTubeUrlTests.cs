using NutriForge.Application.Abstractions;

namespace NutriForge.Application.Tests;

public sealed class YouTubeUrlTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=42s", "dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/watch?app=desktop&v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    public void Parses_the_video_id_from_youtube_urls(string url, string expected)
    {
        Assert.Equal(expected, YouTubeUrl.ParseVideoId(url));
        Assert.True(YouTubeUrl.IsYouTube(url));
    }

    [Theory]
    [InlineData("https://example.com/recipes/pasta")]
    [InlineData("not a url at all")]
    [InlineData("")]
    [InlineData(null)]
    public void Returns_null_for_non_youtube_input(string? url)
    {
        Assert.Null(YouTubeUrl.ParseVideoId(url));
        Assert.False(YouTubeUrl.IsYouTube(url));
    }
}
