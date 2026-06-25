using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using NutriForge.Application.Abstractions;

namespace NutriForge.Application.Recipes;

/// <summary>
/// Deterministically extracts a schema.org/Recipe from a web page's <c>application/ld+json</c> blocks —
/// no AI, no API key. Most recipe sites embed structured data; we map recipeIngredient / recipeYield /
/// totalTime / recipeInstructions / keywords onto the same <see cref="ExtractedRecipe"/> the AI path
/// produces, so the rest of the import flow (resolve → compute → confirm → save) is unchanged. Returns
/// null when no Recipe JSON-LD is present (caller falls back to the AI extractor or pasted text).
/// </summary>
public static partial class JsonLdRecipeParser
{
    [GeneratedRegex(
        """<script[^>]*type\s*=\s*["']application/ld\+json["'][^>]*>(.*?)</script>""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex LdJsonBlock();

    public static ExtractedRecipe? Parse(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        foreach (Match block in LdJsonBlock().Matches(html))
        {
            var json = block.Groups[1].Value.Trim();
            if (json.Length == 0)
            {
                continue;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch (JsonException)
            {
                continue; // a malformed block doesn't sink the page
            }

            using (doc)
            {
                if (FindRecipe(doc.RootElement) is { } recipe)
                {
                    return Map(recipe);
                }
            }
        }

        return null;
    }

    /// <summary>Locate a Recipe node: a bare object, an array of nodes, or a node inside <c>@graph</c>.</summary>
    private static JsonElement? FindRecipe(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                if (IsType(el, "Recipe"))
                {
                    return el;
                }

                if (el.TryGetProperty("@graph", out var graph))
                {
                    return FindRecipe(graph);
                }

                return null;

            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                {
                    if (FindRecipe(item) is { } found)
                    {
                        return found;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    private static bool IsType(JsonElement el, string type)
    {
        if (!el.TryGetProperty("@type", out var t))
        {
            return false;
        }

        if (t.ValueKind == JsonValueKind.String)
        {
            return t.GetString()?.Equals(type, StringComparison.OrdinalIgnoreCase) == true;
        }

        return t.ValueKind == JsonValueKind.Array
            && t.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String
                && x.GetString()?.Equals(type, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static ExtractedRecipe Map(JsonElement r)
    {
        var name = Str(r, "name") ?? "Imported recipe";
        var ingredients = StringList(r, "recipeIngredient")
            .Concat(StringList(r, "ingredients")) // legacy key
            .Select(IngredientLineParser.Parse)
            .ToList();
        var instructions = Instructions(r);
        var servings = Servings(r);
        var minutes = Minutes(r);
        var tags = StringList(r, "keywords")
            .SelectMany(k => k.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(k => k.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ExtractedRecipe(name, servings, minutes, instructions, tags, ingredients);
    }

    private static string? Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>A property that may be a single string, an array of strings, or absent.</summary>
    private static IEnumerable<string> StringList(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v))
        {
            yield break;
        }

        if (v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            if (!string.IsNullOrWhiteSpace(s))
            {
                yield return s.Trim();
            }
        }
        else if (v.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in v.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                {
                    yield return s.Trim();
                }
            }
        }
    }

    /// <summary>recipeInstructions: a string, an array of strings, or HowToStep/HowToSection objects.</summary>
    private static string? Instructions(JsonElement r)
    {
        if (!r.TryGetProperty("recipeInstructions", out var ins))
        {
            return null;
        }

        var steps = new List<string>();
        Collect(ins, steps);
        var joined = string.Join("\n", steps.Where(s => s.Length > 0));
        return joined.Length > 0 ? joined : null;

        static void Collect(JsonElement el, List<string> acc)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.String:
                    var s = el.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        acc.Add(s.Trim());
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in el.EnumerateArray())
                    {
                        Collect(item, acc);
                    }

                    break;
                case JsonValueKind.Object:
                    // HowToSection has nested itemListElement; HowToStep has text/name.
                    if (el.TryGetProperty("itemListElement", out var nested))
                    {
                        Collect(nested, acc);
                    }
                    else if (el.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    {
                        var t = text.GetString();
                        if (!string.IsNullOrWhiteSpace(t))
                        {
                            acc.Add(t.Trim());
                        }
                    }

                    break;
            }
        }
    }

    /// <summary>recipeYield: a number, "4", "4 servings", or an array — take the first integer found.</summary>
    private static int? Servings(JsonElement r)
    {
        if (!r.TryGetProperty("recipeYield", out var y))
        {
            return null;
        }

        return y.ValueKind switch
        {
            JsonValueKind.Number when y.TryGetInt32(out var n) => n,
            JsonValueKind.String => FirstInt(y.GetString()),
            JsonValueKind.Array => y.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) ? n : FirstInt(e.GetString()))
                .FirstOrDefault(v => v is > 0),
            _ => null,
        };
    }

    private static int? FirstInt(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return null;
        }

        var m = Regex.Match(s, @"\d+");
        return m.Success && int.TryParse(m.Value, out var n) ? n : null;
    }

    /// <summary>totalTime (or cookTime + prepTime) as an ISO-8601 duration ⇒ whole minutes.</summary>
    private static int? Minutes(JsonElement r)
    {
        var total = Duration(r, "totalTime");
        if (total is { } t)
        {
            return t;
        }

        var cook = Duration(r, "cookTime") ?? 0;
        var prep = Duration(r, "prepTime") ?? 0;
        var sum = cook + prep;
        return sum > 0 ? sum : null;
    }

    private static int? Duration(JsonElement r, string prop)
    {
        var iso = Str(r, prop);
        if (string.IsNullOrWhiteSpace(iso))
        {
            return null;
        }

        try
        {
            return (int)Math.Round(XmlConvert.ToTimeSpan(iso).TotalMinutes);
        }
        catch (FormatException)
        {
            return FirstInt(iso); // some sites put plain "30" — better than nothing
        }
    }
}
