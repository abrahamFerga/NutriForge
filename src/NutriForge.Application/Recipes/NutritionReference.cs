using NutriForge.Domain.Common;
using NutriForge.Domain.Recipes;

namespace NutriForge.Application.Recipes;

/// <summary>
/// A deterministic, curated reference of per-100g macros for common cooking ingredients (#101). It is the
/// fallback nutrition source when a recipe ingredient doesn't match a canonical catalog <c>Ingredient</c>:
/// the values come from this fixed table — never from the LLM — so ADR-0004 (the model never owns a
/// number) holds even for AI-generated or freely-imported recipes. This is what lets a recipe with
/// ordinary ingredients (paprika, bell pepper, lime …) compute its nutrition instead of showing 0 kcal.
/// </summary>
public static class NutritionReference
{
    /// <summary>One reference ingredient: per-100g macros plus optional volume density and per-count mass.</summary>
    private sealed record Ref(
        string Name, double Kcal, double Protein, double Fat, double Carb,
        double? DensityGPerMl = null, double? GramsPerCount = null, string[]? Aliases = null)
    {
        /// <summary>The canonical name plus aliases, lower-cased — the strings an input line is matched against.</summary>
        public string[] MatchTokens { get; } =
            [Name, .. (Aliases ?? []).Select(a => a.ToLowerInvariant())];
    }

    /// <summary>
    /// Resolve an ingredient line to grams + macros from the reference table, or null when the name is
    /// unknown. A line whose amount can't be converted (no unit/count) resolves to 0 g — a negligible
    /// contribution — so a known-but-unquantified garnish never blocks the whole recipe from computing.
    /// </summary>
    public static (double Grams, Macros Macros)? Resolve(string name, double quantity, string? unit)
    {
        var match = Match(name);
        if (match is null)
        {
            return null;
        }

        var grams = UnitConverter.ToGrams(quantity, unit, match.DensityGPerMl, match.GramsPerCount)
                    ?? (match.GramsPerCount is { } gpc ? quantity * gpc : 0.0);
        grams = Math.Max(0, grams);

        var per100g = new Macros(match.Kcal, match.Protein, match.Fat, match.Carb);
        return (Math.Round(grams, 1), per100g.Scale(grams / 100.0).Rounded());
    }

    /// <summary>True when the ingredient name is known to the reference table (exposed for diagnostics/tests).</summary>
    public static bool IsKnown(string name) => Match(name) is not null;

    private static Ref? Match(string rawName)
    {
        var n = rawName.Trim().ToLowerInvariant();
        if (n.Length == 0)
        {
            return null;
        }

        Ref? best = null;
        var bestScore = 0;
        var bestLen = 0;

        foreach (var r in Table)
        {
            foreach (var token in r.MatchTokens)
            {
                var score = ScoreToken(n, token);
                // Prefer a stronger match; on a tie prefer the longer (more specific) token.
                if (score > bestScore || (score == bestScore && score > 0 && token.Length > bestLen))
                {
                    best = r;
                    bestScore = score;
                    bestLen = token.Length;
                }
            }
        }

        return bestScore > 0 ? best : null;
    }

    // 3 = the line IS this token; 2 = the line contains this token as a substring (≥4 chars to avoid noise).
    private static int ScoreToken(string name, string token)
    {
        if (name == token)
        {
            return 3;
        }

        return token.Length >= 4 && name.Contains(token, StringComparison.Ordinal) ? 2 : 0;
    }

    // Per-100g values are approximate (USDA-style) and intended for planning, not clinical accuracy.
    private static readonly Ref[] Table =
    [
        // Proteins
        new("chicken breast", 165, 31, 3.6, 0, GramsPerCount: 170),
        new("chicken thigh", 209, 26, 11, 0, GramsPerCount: 120),
        new("chicken", 190, 27, 8, 0),
        new("ground beef", 250, 26, 17, 0, Aliases: ["beef mince", "minced beef", "mince"]),
        new("beef", 271, 25, 19, 0, Aliases: ["steak"]),
        new("pork", 242, 27, 14, 0),
        new("bacon", 541, 37, 42, 1.4),
        new("turkey", 189, 29, 7, 0),
        new("salmon", 208, 20, 13, 0),
        new("tuna", 132, 28, 1, 0),
        new("cod", 82, 18, 0.7, 0, Aliases: ["white fish"]),
        new("shrimp", 99, 24, 0.3, 0.2, Aliases: ["prawn", "prawns"]),
        new("egg", 143, 13, 9.5, 0.7, GramsPerCount: 50),
        new("tofu", 76, 8, 4.8, 1.9),

        // Dairy
        new("milk", 61, 3.2, 3.3, 4.8, DensityGPerMl: 1.03),
        new("greek yogurt", 59, 10, 0.4, 3.6),
        new("yogurt", 61, 3.5, 3.3, 4.7, Aliases: ["yoghurt"]),
        new("cheddar", 403, 25, 33, 1.3, Aliases: ["cheddar cheese"]),
        new("mozzarella", 280, 28, 17, 3),
        new("parmesan", 431, 38, 29, 4),
        new("feta", 264, 14, 21, 4),
        new("cheese", 350, 23, 28, 2),
        new("butter", 717, 0.9, 81, 0.1, DensityGPerMl: 0.91),
        new("cream", 340, 2, 36, 3, DensityGPerMl: 1.0),

        // Grains & starches
        new("white rice", 360, 7, 0.7, 80, Aliases: ["rice"]),
        new("brown rice", 367, 8, 2.9, 76),
        new("cooked rice", 130, 2.7, 0.3, 28),
        new("pasta", 371, 13, 1.5, 75, Aliases: ["spaghetti", "penne", "macaroni"]),
        new("noodles", 138, 5, 2, 25),
        new("bread", 265, 9, 3.2, 49),
        new("tortilla", 304, 8, 7, 51, Aliases: ["wrap"]),
        new("oats", 389, 17, 7, 66, Aliases: ["oatmeal"]),
        new("quinoa", 368, 14, 6, 64),
        new("couscous", 376, 13, 0.6, 77),
        new("flour", 364, 10, 1, 76, DensityGPerMl: 0.53),
        new("cornstarch", 381, 0.3, 0.1, 91, DensityGPerMl: 0.5, Aliases: ["corn starch", "cornflour", "corn flour"]),
        new("potato", 77, 2, 0.1, 17, GramsPerCount: 150),
        new("sweet potato", 86, 1.6, 0.1, 20, GramsPerCount: 130),

        // Vegetables
        new("onion", 40, 1.1, 0.1, 9.3, GramsPerCount: 110, Aliases: ["brown onion", "red onion", "yellow onion", "white onion", "spring onion", "green onion", "scallion"]),
        new("garlic powder", 331, 17, 0.7, 73, DensityGPerMl: 0.5),
        new("onion powder", 341, 10, 1, 79, DensityGPerMl: 0.5),
        new("garlic", 149, 6.4, 0.5, 33, GramsPerCount: 3, Aliases: ["garlic clove", "clove"]),
        new("bell pepper", 31, 1, 0.3, 6, GramsPerCount: 120, Aliases: ["red bell pepper", "yellow bell pepper", "green bell pepper", "capsicum", "pepper bell"]),
        new("tomato", 18, 0.9, 0.2, 3.9, GramsPerCount: 123, Aliases: ["tomatoes"]),
        new("tomato paste", 82, 4, 0.5, 19, DensityGPerMl: 1.1),
        new("tomato sauce", 29, 1.3, 0.2, 6, DensityGPerMl: 1.05, Aliases: ["passata", "marinara"]),
        new("jalapeno", 29, 0.9, 0.4, 6.5, GramsPerCount: 14, Aliases: ["jalapeño"]),
        new("carrot", 41, 0.9, 0.2, 10, GramsPerCount: 60),
        new("broccoli", 34, 2.8, 0.4, 7),
        new("spinach", 23, 2.9, 0.4, 3.6),
        new("lettuce", 15, 1.4, 0.2, 2.9),
        new("cucumber", 15, 0.7, 0.1, 3.6),
        new("mushroom", 22, 3.1, 0.3, 3.3, Aliases: ["mushrooms"]),
        new("zucchini", 17, 1.2, 0.3, 3.1, Aliases: ["courgette"]),
        new("corn", 86, 3.2, 1.2, 19, Aliases: ["sweetcorn"]),
        new("peas", 81, 5, 0.4, 14),
        new("green beans", 31, 1.8, 0.2, 7),
        new("cabbage", 25, 1.3, 0.1, 6),
        new("cauliflower", 25, 1.9, 0.3, 5),
        new("celery", 16, 0.7, 0.2, 3, GramsPerCount: 40),
        new("avocado", 160, 2, 15, 9, GramsPerCount: 150),
        new("ginger", 80, 1.8, 0.8, 18, GramsPerCount: 15),

        // Fruit
        new("lime", 30, 0.7, 0.2, 11, GramsPerCount: 67, Aliases: ["lime wedge", "lime juice"]),
        new("lemon", 29, 1.1, 0.3, 9, GramsPerCount: 58, Aliases: ["lemon juice"]),
        new("apple", 52, 0.3, 0.2, 14, GramsPerCount: 180),
        new("banana", 89, 1.1, 0.3, 23, GramsPerCount: 118),
        new("orange", 47, 0.9, 0.1, 12, GramsPerCount: 130),
        new("strawberry", 32, 0.7, 0.3, 8, Aliases: ["strawberries", "berries"]),
        new("blueberry", 57, 0.7, 0.3, 14, Aliases: ["blueberries"]),

        // Fats & oils
        new("olive oil", 884, 0, 100, 0, DensityGPerMl: 0.91),
        new("coconut oil", 862, 0, 100, 0, DensityGPerMl: 0.92),
        new("sesame oil", 884, 0, 100, 0, DensityGPerMl: 0.92),
        new("oil", 884, 0, 100, 0, DensityGPerMl: 0.91, Aliases: ["vegetable oil", "sunflower oil", "canola oil"]),

        // Legumes & nuts
        new("black beans", 132, 9, 0.5, 24, Aliases: ["beans"]),
        new("chickpeas", 164, 9, 2.6, 27, Aliases: ["garbanzo"]),
        new("lentils", 116, 9, 0.4, 20),
        new("almonds", 579, 21, 50, 22, Aliases: ["almond", "nuts"]),
        new("peanut butter", 588, 25, 50, 20),

        // Condiments, spices & liquids
        new("salt", 0, 0, 0, 0, DensityGPerMl: 1.2, Aliases: ["sea salt", "sea salt flakes", "kosher salt", "table salt"]),
        new("black pepper", 251, 10, 3, 64, DensityGPerMl: 0.5, Aliases: ["pepper"]),
        new("paprika", 282, 14, 13, 54, DensityGPerMl: 0.46, Aliases: ["smoked paprika"]),
        new("chili powder", 282, 13, 14, 50, DensityGPerMl: 0.5, Aliases: ["chilli powder", "chile powder"]),
        new("ground cumin", 375, 18, 22, 44, DensityGPerMl: 0.5, Aliases: ["cumin"]),
        new("ground coriander", 298, 12, 18, 55, DensityGPerMl: 0.5),
        new("coriander", 23, 2.1, 0.5, 3.7, Aliases: ["cilantro", "fresh coriander"]),
        new("oregano", 265, 9, 4, 69, DensityGPerMl: 0.3, Aliases: ["fresh oregano", "dried oregano"]),
        new("basil", 23, 3.2, 0.6, 2.7, Aliases: ["fresh basil"]),
        new("parsley", 36, 3, 0.8, 6, Aliases: ["fresh parsley"]),
        new("cinnamon", 247, 4, 1.2, 81, DensityGPerMl: 0.5),
        new("soy sauce", 53, 8, 0, 5, DensityGPerMl: 1.1),
        new("honey", 304, 0.3, 0, 82, DensityGPerMl: 1.42),
        new("sugar", 387, 0, 0, 100, DensityGPerMl: 0.85, Aliases: ["brown sugar", "white sugar"]),
        new("ketchup", 101, 1, 0.1, 27, DensityGPerMl: 1.1),
        new("mustard", 66, 4, 4, 5, DensityGPerMl: 1.1),
        new("mayonnaise", 680, 1, 75, 0.6, DensityGPerMl: 0.91, Aliases: ["mayo"]),
        new("vinegar", 18, 0, 0, 0.9, DensityGPerMl: 1.01),
        new("chicken stock", 4, 0.5, 0.2, 0.5, DensityGPerMl: 1.0, Aliases: ["chicken broth", "stock", "broth", "vegetable stock", "beef stock"]),
        new("water", 0, 0, 0, 0, DensityGPerMl: 1.0),
    ];
}
