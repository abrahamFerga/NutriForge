using System.Globalization;
using NutriForge.Application.Abstractions;

namespace NutriForge.Application.Recipes;

/// <summary>
/// Deterministically splits a free-text ingredient line ("2 cups flour", "1 1/2 tbsp olive oil",
/// "salt to taste") into quantity + unit + name — no AI. Best-effort: an unparseable quantity yields
/// 0 (the line is then flagged 'unresolved' downstream); the catalog resolver still matches the name.
/// </summary>
public static class IngredientLineParser
{
    // Common recipe units (singular forms; trailing 's' is tolerated). Kept small + deterministic.
    private static readonly HashSet<string> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        "cup", "tablespoon", "tbsp", "tbs", "teaspoon", "tsp", "ounce", "oz", "pound", "lb", "lbs",
        "gram", "g", "kg", "kilogram", "milligram", "mg", "ml", "milliliter", "millilitre", "l", "liter",
        "litre", "pinch", "dash", "clove", "can", "slice", "piece", "stick", "stalk", "sprig", "head",
        "bunch", "package", "pkg", "quart", "qt", "pint", "pt", "gallon", "gal", "fl",
    };

    // Unicode vulgar fractions → decimal.
    private static readonly Dictionary<char, double> Vulgar = new()
    {
        ['½'] = 0.5,
        ['⅓'] = 1.0 / 3,
        ['⅔'] = 2.0 / 3,
        ['¼'] = 0.25,
        ['¾'] = 0.75,
        ['⅛'] = 0.125,
        ['⅜'] = 0.375,
        ['⅝'] = 0.625,
        ['⅞'] = 0.875,
    };

    public static ExtractedIngredient Parse(string? rawText)
    {
        var raw = (rawText ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return new ExtractedIngredient(raw, raw, null, null);
        }

        var tokens = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var i = 0;

        // 1) Leading quantity: a run of numeric tokens (mixed numbers "1 1/2", fractions, decimals, vulgar).
        double quantity = 0;
        var sawNumber = false;
        while (i < tokens.Length && TryNumber(tokens[i], out var n))
        {
            quantity += n;
            sawNumber = true;
            i++;
        }

        // 2) Optional unit immediately after the quantity.
        string? unit = null;
        if (sawNumber && i < tokens.Length && IsUnit(tokens[i]))
        {
            unit = tokens[i].TrimEnd('.', ',');
            i++;
        }

        // 3) The remainder is the ingredient name (drop a leading "of", e.g. "1 cup of sugar").
        if (i < tokens.Length && tokens[i].Equals("of", StringComparison.OrdinalIgnoreCase))
        {
            i++;
        }

        var name = string.Join(' ', tokens[i..]).Trim();
        if (name.Length == 0)
        {
            name = raw; // nothing left (e.g. the whole line was a name) — keep the original
        }

        return new ExtractedIngredient(raw, name, sawNumber ? quantity : null, unit);
    }

    private static bool IsUnit(string token)
    {
        var t = token.TrimEnd('.', ',').TrimEnd('s'); // tolerate plural
        return Units.Contains(t) || Units.Contains(token.TrimEnd('.', ','));
    }

    /// <summary>Parse "2", "2.5", "1/2", or a single vulgar fraction "½". Mixed numbers sum across tokens.</summary>
    private static bool TryNumber(string token, out double value)
    {
        value = 0;
        var t = token.Trim();
        if (t.Length == 0)
        {
            return false;
        }

        // A single vulgar-fraction char (optionally appended to digits, e.g. "1½").
        if (Vulgar.TryGetValue(t[^1], out var frac))
        {
            var lead = t[..^1];
            if (lead.Length == 0)
            {
                value = frac;
                return true;
            }

            if (double.TryParse(lead, NumberStyles.Any, CultureInfo.InvariantCulture, out var whole))
            {
                value = whole + frac;
                return true;
            }

            return false;
        }

        // a/b fraction.
        var slash = t.IndexOf('/');
        if (slash > 0 && slash < t.Length - 1)
        {
            if (double.TryParse(t[..slash], NumberStyles.Any, CultureInfo.InvariantCulture, out var num)
                && double.TryParse(t[(slash + 1)..], NumberStyles.Any, CultureInfo.InvariantCulture, out var den)
                && den != 0)
            {
                value = num / den;
                return true;
            }

            return false;
        }

        return double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }
}
