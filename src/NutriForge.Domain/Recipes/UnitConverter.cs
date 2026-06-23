namespace NutriForge.Domain.Recipes;

/// <summary>
/// Deterministic unit → grams conversion. Mass units convert directly; volume units convert via the
/// ingredient density (default 1.0 g/ml); "count" units use the ingredient's grams-per-count. Pure.
/// </summary>
public static class UnitConverter
{
    // Volume units in millilitres.
    private static readonly Dictionary<string, double> VolumeMl = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ml"] = 1,
        ["milliliter"] = 1,
        ["millilitre"] = 1,
        ["l"] = 1000,
        ["liter"] = 1000,
        ["litre"] = 1000,
        ["tsp"] = 5,
        ["teaspoon"] = 5,
        ["tbsp"] = 15,
        ["tablespoon"] = 15,
        ["cup"] = 240,
        ["cups"] = 240,
        ["floz"] = 29.57,
        ["fl oz"] = 29.57,
        ["pint"] = 473,
        ["quart"] = 946,
    };

    // Mass units in grams.
    private static readonly Dictionary<string, double> MassG = new(StringComparer.OrdinalIgnoreCase)
    {
        ["g"] = 1,
        ["gram"] = 1,
        ["grams"] = 1,
        ["gr"] = 1,
        ["kg"] = 1000,
        ["kilogram"] = 1000,
        ["mg"] = 0.001,
        ["oz"] = 28.35,
        ["ounce"] = 28.35,
        ["lb"] = 453.6,
        ["lbs"] = 453.6,
        ["pound"] = 453.6,
    };

    /// <summary>
    /// Convert a quantity in <paramref name="unit"/> to grams. Returns null when it can't be
    /// resolved deterministically (e.g. a count unit with no grams-per-count known).
    /// </summary>
    public static double? ToGrams(double quantity, string? unit, double? densityGPerMl, double? gramsPerCount)
    {
        var u = (unit ?? string.Empty).Trim().ToLowerInvariant();

        if (u.Length == 0 || u is "count" or "piece" or "pieces" or "unit" or "whole" or "x")
        {
            return gramsPerCount is { } g ? quantity * g : null;
        }

        if (MassG.TryGetValue(u, out var massFactor))
        {
            return quantity * massFactor;
        }

        if (VolumeMl.TryGetValue(u, out var ml))
        {
            return quantity * ml * (densityGPerMl ?? 1.0);
        }

        // Unknown unit (e.g. "clove", "slice") — fall back to grams-per-count if we have it.
        return gramsPerCount is { } gpc ? quantity * gpc : null;
    }
}
