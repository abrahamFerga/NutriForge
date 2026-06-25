using NutriForge.Domain.Recipes;

namespace NutriForge.Domain.Tests;

/// <summary>
/// The web-import dedup key (#91): a normalized form of the source URL (scheme, case, trailing slash,
/// and fragment all dropped; host + path + query kept), and null for YouTube (its video id dedups) or
/// hand-authored recipes.
/// </summary>
public class RecipeSourceKeyTests
{
    [Theory]
    [InlineData("https://Example.com/Recipe/", null, "example.com/recipe")]       // scheme/case/trailing-slash
    [InlineData("http://example.com/r?id=5#frag", null, "example.com/r?id=5")]    // scheme + fragment dropped, query kept
    [InlineData("HTTPS://RECIPES.EXAMPLE.COM/abc", null, "recipes.example.com/abc")]
    public void NormalizeSourceKey_canonicalizes_web_urls(string url, string? videoId, string expected)
    {
        Assert.Equal(expected, Recipe.NormalizeSourceKey(videoId, url));
    }

    [Fact]
    public void NormalizeSourceKey_is_null_for_youtube_and_for_no_url()
    {
        Assert.Null(Recipe.NormalizeSourceKey("dQw4w9WgXcQ", "https://youtube.com/watch?v=dQw4w9WgXcQ"));
        Assert.Null(Recipe.NormalizeSourceKey(null, null));
        Assert.Null(Recipe.NormalizeSourceKey(null, "   "));
    }

    [Fact]
    public void Two_urls_differing_only_by_case_or_trailing_slash_share_a_key()
    {
        var a = Recipe.NormalizeSourceKey(null, "https://site.com/recipes/lasagna");
        var b = Recipe.NormalizeSourceKey(null, "http://SITE.com/recipes/lasagna/");
        Assert.Equal(a, b);
        Assert.NotNull(a);
    }
}
