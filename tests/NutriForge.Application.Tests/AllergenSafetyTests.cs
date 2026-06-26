using NutriForge.Application.DietGen;
using NutriForge.Domain.Common;
using NutriForge.Domain.Recipes;

namespace NutriForge.Application.Tests;

/// <summary>
/// Adversarial review of the allergen-safety path (#60). The exclusion gate matches keywords as
/// substrings of recipe/ingredient names; on its own that misses derivatives (declaring "milk" wouldn't
/// catch "butter"). These tests pin the <see cref="AllergenOntology"/> expansion that closes the gap and
/// fuzz the end-to-end property: a declared allergen — or any of its known derivatives — can never appear
/// in the filtered pool, the generated plan, or survive the defense-in-depth re-check.
/// </summary>
public sealed class AllergenSafetyTests
{
    private static Recipe Rec(string name, params string[] ingredients)
    {
        var r = new Recipe { Name = name, Servings = 1, TotalMinutes = 15, Tags = [] };
        foreach (var ing in ingredients)
        {
            var ri = new RecipeIngredient { IngredientName = ing, Quantity = 100, Unit = "g" };
            ri.SetContribution(100, new Macros(120, 8, 4, 12));
            r.AddIngredient(ri);
        }

        r.RecomputeNutrition();
        return r;
    }

    // (declared allergen, a derivative ingredient that does NOT contain the declared word as a substring)
    public static TheoryData<string, string> Derivatives() => new()
    {
        { "milk", "butter" },
        { "milk", "cheddar cheese" },
        { "dairy", "heavy cream" },
        { "dairy", "whey protein" },
        { "egg", "mayonnaise" },
        { "tree nuts", "almonds" },
        { "nuts", "cashew" },
        { "soy", "tofu" },
        { "soy", "edamame" },
        { "gluten", "semolina" },
        { "wheat", "couscous" },
        { "fish", "anchovy" },
        { "shellfish", "shrimp" },
        { "sesame", "tahini" },
    };

    [Theory]
    [MemberData(nameof(Derivatives))]
    public void A_declared_allergen_filters_recipes_made_of_its_derivatives(string allergen, string derivative)
    {
        // Sanity: the bare substring gate would NOT catch this derivative (that's the gap #60 closes).
        Assert.False(derivative.Contains(allergen, StringComparison.OrdinalIgnoreCase));

        var recipe = Rec("Mystery dish", derivative);
        var exclude = AllergenOntology.Expand([allergen]);
        var intent = new DietIntent(2000, null, null, [.. exclude], null, 3, 7);

        var pool = RecipeFilter.Filter([recipe], intent, diet: null);

        Assert.Empty(pool); // the derivative is caught via expansion
        Assert.False(PlanVerifier.AllergensClear(
            [new PlannedSlot { Day = 1, Meal = MealSlot.Lunch, Recipe = recipe }], [.. exclude]));
    }

    [Fact]
    public void Expansion_always_keeps_the_literal_declaration_even_when_unknown()
    {
        // An allergen not in the ontology (e.g. "mango") still excludes itself — expansion only adds.
        var exclude = AllergenOntology.Expand([" Mango "]);
        Assert.Contains("Mango", exclude);

        var mango = Rec("Mango sticky rice", "mango", "rice");
        var pool = RecipeFilter.Filter([mango], new DietIntent(2000, null, null, [.. exclude], null, 3, 7), null);
        Assert.Empty(pool);
    }

    [Fact]
    public void Blank_declarations_never_exclude_everything()
    {
        var exclude = AllergenOntology.Expand(["", "  ", "\t"]);
        Assert.Empty(exclude);

        var safe = Rec("Plain rice", "rice");
        var pool = RecipeFilter.Filter([safe], new DietIntent(2000, null, null, [.. exclude], null, 3, 7), null);
        Assert.Single(pool); // nothing excluded
    }

    /// <summary>
    /// Fuzz: across many randomized recipe pools, a declared allergen's derivative never survives the
    /// FILTER, and whatever the FILTER passes is always clean under the defense-in-depth re-check. Seeded
    /// so failures reproduce.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(1337)]
    public void Fuzz_no_declared_allergen_derivative_ever_survives_the_gate(int seed)
    {
        var rng = new Random(seed);
        string[] allergens = ["milk", "egg", "soy", "gluten", "fish", "shellfish", "tree nuts", "sesame"];
        string[] unsafeIngredients =
        [
            "butter", "cheese", "cream", "whey", "mayonnaise", "tofu", "edamame", "semolina", "couscous",
            "anchovy", "shrimp", "almonds", "cashew", "tahini", "yogurt", "seitan", "prawn",
        ];
        string[] safeIngredients = ["rice", "chicken breast", "potato", "carrot", "olive oil", "spinach", "apple"];

        for (var iter = 0; iter < 200; iter++)
        {
            var declared = allergens[rng.Next(allergens.Length)];
            var exclude = AllergenOntology.Expand([declared]);
            var intent = new DietIntent(2000, null, null, [.. exclude], null, 3, 7);

            // Build a random pool mixing safe foods with some unsafe derivatives.
            var recipes = new List<Recipe>();
            var count = rng.Next(3, 9);
            for (var i = 0; i < count; i++)
            {
                var ings = new List<string> { safeIngredients[rng.Next(safeIngredients.Length)] };
                if (rng.NextDouble() < 0.5)
                {
                    ings.Add(unsafeIngredients[rng.Next(unsafeIngredients.Length)]);
                }

                recipes.Add(Rec($"dish-{i}", [.. ings]));
            }

            var pool = RecipeFilter.Filter(recipes, intent, diet: null);

            // Property 1: nothing in the surviving pool trips the (expanded) exclusion gate.
            Assert.True(PlanVerifier.AllergensClear(
                [.. pool.Select(r => new PlannedSlot { Day = 1, Meal = MealSlot.Lunch, Recipe = r })],
                [.. exclude]),
                $"seed={seed} iter={iter} declared={declared}: an excluded term survived the filter");

            // Property 2: any recipe that DID contain an excluded term was removed.
            foreach (var r in recipes.Where(r => exclude.Any(k => RecipeFilter.Contains(r, k))))
            {
                Assert.DoesNotContain(pool, p => ReferenceEquals(p, r));
            }
        }
    }
}
