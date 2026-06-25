using System.Net;
using NutriForge.Application.Recipes;

namespace NutriForge.Application.Tests;

/// <summary>
/// The deterministic (no-AI, no-key) recipe-import building blocks (#91): the ingredient-line parser,
/// the schema.org/Recipe JSON-LD parser, and the SSRF address guard.
/// </summary>
public sealed class RecipeImportParsingTests
{
    [Theory]
    [InlineData("2 cups flour", 2.0, "cups", "flour")]
    [InlineData("1 1/2 tbsp olive oil", 1.5, "tbsp", "olive oil")]
    [InlineData("1 cup of sugar", 1.0, "cup", "sugar")]      // drops the "of"
    [InlineData("200 g chicken breast", 200.0, "g", "chicken breast")]
    [InlineData("½ tsp vanilla", 0.5, "tsp", "vanilla")]      // unicode fraction
    public void IngredientLine_parses_quantity_unit_and_name(string line, double qty, string unit, string name)
    {
        var parsed = IngredientLineParser.Parse(line);
        Assert.Equal(qty, parsed.Quantity);
        Assert.Equal(unit, parsed.Unit);
        Assert.Equal(name, parsed.Name);
    }

    [Fact]
    public void IngredientLine_without_a_quantity_keeps_the_whole_name()
    {
        var parsed = IngredientLineParser.Parse("salt to taste");
        Assert.Null(parsed.Quantity);
        Assert.Null(parsed.Unit);
        Assert.Equal("salt to taste", parsed.Name);
    }

    [Fact]
    public void JsonLd_parses_a_schema_org_recipe_from_a_page()
    {
        const string html = """
        <html><head>
        <script type="application/ld+json">
        {"@context":"https://schema.org","@type":"Recipe","name":"Test Pancakes",
         "recipeYield":"4 servings","totalTime":"PT25M",
         "recipeIngredient":["2 cups flour","1 1/2 cups milk","2 eggs"],
         "recipeInstructions":[{"@type":"HowToStep","text":"Mix the batter."},{"@type":"HowToStep","text":"Cook on a griddle."}],
         "keywords":"breakfast, easy"}
        </script></head><body>ignored</body></html>
        """;

        var recipe = JsonLdRecipeParser.Parse(html);

        Assert.NotNull(recipe);
        Assert.Equal("Test Pancakes", recipe!.Name);
        Assert.Equal(4, recipe.Servings);
        Assert.Equal(25, recipe.TotalMinutes);
        Assert.Equal(3, recipe.Ingredients.Count);
        Assert.Equal("flour", recipe.Ingredients[0].Name);
        Assert.Contains("Mix the batter.", recipe.Instructions);
        Assert.Contains("Cook on a griddle.", recipe.Instructions);
        Assert.Contains("breakfast", recipe.Tags);
        Assert.Contains("easy", recipe.Tags);
    }

    [Fact]
    public void JsonLd_finds_a_recipe_inside_an_at_graph()
    {
        const string html = """
        <script type="application/ld+json">
        {"@context":"https://schema.org","@graph":[
          {"@type":"WebSite","name":"Site"},
          {"@type":"Recipe","name":"Graph Soup","recipeIngredient":["1 onion"],"recipeYield":2}
        ]}</script>
        """;

        var recipe = JsonLdRecipeParser.Parse(html);

        Assert.NotNull(recipe);
        Assert.Equal("Graph Soup", recipe!.Name);
        Assert.Equal(2, recipe.Servings);
    }

    [Fact]
    public void JsonLd_returns_null_when_a_page_has_no_recipe()
    {
        Assert.Null(JsonLdRecipeParser.Parse("<html><body>just a blog post</body></html>"));
        Assert.Null(JsonLdRecipeParser.Parse("<script type=\"application/ld+json\">{\"@type\":\"Article\"}</script>"));
        Assert.Null(JsonLdRecipeParser.Parse(""));
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("169.254.169.254", true)]   // cloud metadata
    [InlineData("100.64.0.1", true)]        // CGNAT
    [InlineData("0.0.0.0", true)]
    [InlineData("8.8.8.8", false)]          // public
    [InlineData("1.1.1.1", false)]
    [InlineData("::1", true)]               // IPv6 loopback
    [InlineData("fe80::1", true)]           // IPv6 link-local
    [InlineData("fc00::1", true)]           // IPv6 ULA
    [InlineData("::ffff:127.0.0.1", true)]  // mapped loopback
    [InlineData("2606:4700:4700::1111", false)] // public IPv6
    public void SsrfGuard_blocks_non_public_addresses(string ip, bool blocked)
    {
        Assert.Equal(blocked, PrivateNetworkGuard.IsBlocked(IPAddress.Parse(ip)));
    }
}
