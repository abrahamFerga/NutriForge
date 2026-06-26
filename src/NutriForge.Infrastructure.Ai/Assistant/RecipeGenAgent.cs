using System.Globalization;
using System.Text;
using NutriForge.Application.Abstractions;

namespace NutriForge.Infrastructure.Ai.Assistant;

/// <summary>
/// Generates original recipes from a brief (#101). The model writes titles, steps and ingredient lines
/// only — it NEVER emits a nutrition number; the catalog + reference resolver own every macro (ADR-0004).
/// It is told to prefer common, widely-available ingredients so resolution succeeds, and to respect the
/// diet and the excluded (allergen) list — which is also re-checked deterministically downstream.
/// </summary>
public sealed class RecipeGenAgent(IAssistantAgentFactory factory) : IRecipeGenAgent
{
    private const string Prompt =
        """
        You are a recipe-writing chef for a calorie- and macro-tracking app. Generate the requested number
        of DISTINCT, realistic, home-cookable recipes that match the brief.

        For EACH recipe return: a short title; the number of servings it yields; total time in minutes; the
        method as a single clear step-by-step instructions string (numbered steps); a few lower-case tags
        (cuisine / diet / meal-type); and every ingredient line. For each ingredient return: a verbatim
        rawText (e.g. "200 g chicken breast"), a plain singular name (e.g. "chicken breast", "olive oil"),
        the numeric quantity, and the unit (g, ml, cup, tbsp, tsp, clove, slice) or null for a whole item.

        Hard rules:
        - NEVER output any calorie, macro, or nutrition number. You only write recipes; the app computes
          every quantity of energy and protein/fat/carbs itself.
        - Prefer COMMON, widely-available whole ingredients and simple pantry staples, and always give a
          concrete quantity + unit for each, so the app can resolve nutrition. Avoid obscure brand items.
        - Respect the requested diet, and DO NOT use any ingredient in the exclusion list or anything
          derived from it (these are allergies — treat as safety-critical).
        - The recipe MUST suit the requested meal type. A breakfast must be a dish people genuinely eat
          for breakfast (eggs, omelettes, oats, yogurt bowls, pancakes, breakfast tacos/burritos) — never
          a lunch or dinner entrée relabelled as breakfast. When a cuisine or style is requested, choose
          THAT cuisine's breakfast dishes (e.g. for Mexican breakfast: huevos rancheros, chilaquiles,
          breakfast tacos, molletes). Lunch and dinner are fuller mains; a snack is small and simple
          (a handful, a single piece, a small bowl), not a full meal.
        - Aim each recipe near the target calories per serving if one is given, using portion sizes (you do
          not state the numbers — just choose sensible amounts).
        - Make the recipes varied (different proteins / cuisines / methods); do not repeat a recipe.
        """;

    public bool IsConfigured => factory.IsConfigured;

    public async Task<IReadOnlyList<ExtractedRecipe>> GenerateAsync(RecipeBrief brief, int count, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(brief);
        if (!factory.IsConfigured)
        {
            return [];
        }

        var n = Math.Clamp(count, 1, 12);
        var agent = factory.CreateBareAgent(Prompt);
        var response = await agent.RunAsync<GeneratedRecipesJson>(BuildBrief(brief, n), cancellationToken: ct).ConfigureAwait(false);

        return response.Result?.Recipes?
            .Where(r => !string.IsNullOrWhiteSpace(r.Name) && r.Ingredients is { Count: > 0 })
            .Take(n)
            .Select(r => new ExtractedRecipe(
                r.Name!.Trim(),
                r.Servings is > 0 ? r.Servings : brief.Servings,
                r.TotalMinutes is > 0 ? r.TotalMinutes : null,
                string.IsNullOrWhiteSpace(r.Instructions) ? null : r.Instructions!.Trim(),
                (r.Tags ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim().ToLowerInvariant()).ToList(),
                (r.Ingredients ?? [])
                    .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                    .Select(i => new ExtractedIngredient(
                        string.IsNullOrWhiteSpace(i.RawText) ? i.Name!.Trim() : i.RawText!.Trim(),
                        i.Name!.Trim(),
                        i.Quantity is > 0 ? i.Quantity : null,
                        string.IsNullOrWhiteSpace(i.Unit) ? null : i.Unit!.Trim()))
                    .ToList()))
            .ToList() ?? [];
    }

    private static string BuildBrief(RecipeBrief b, int count)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"Generate {count} recipe(s).");
        sb.Append(CultureInfo.InvariantCulture, $" Servings each: {b.Servings}.");
        if (!string.IsNullOrWhiteSpace(b.MealType))
        {
            sb.Append(CultureInfo.InvariantCulture, $" Meal type: {b.MealType}.");
        }
        if (!string.IsNullOrWhiteSpace(b.DietSlug))
        {
            sb.Append(CultureInfo.InvariantCulture, $" Diet: {b.DietSlug}.");
        }
        if (!string.IsNullOrWhiteSpace(b.Cuisine))
        {
            sb.Append(CultureInfo.InvariantCulture, $" Cuisine / style: {b.Cuisine}.");
        }
        if (b.TargetKcal is > 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $" Aim for roughly {b.TargetKcal:F0} kcal per serving.");
        }
        if (b.MaxPrepMinutes is > 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $" No more than {b.MaxPrepMinutes} minutes total.");
        }
        if (b.Exclude is { Count: > 0 })
        {
            sb.Append(CultureInfo.InvariantCulture, $" MUST NOT contain (allergens/excluded): {string.Join(", ", b.Exclude)}.");
        }
        return sb.ToString();
    }

    // Structured-output targets — settable members so MAF can deserialize the model's JSON.
    internal sealed class GeneratedRecipesJson
    {
        public List<RecipeJson> Recipes { get; set; } = [];
    }

    internal sealed class RecipeJson
    {
        public string? Name { get; set; }
        public int? Servings { get; set; }
        public int? TotalMinutes { get; set; }
        public string? Instructions { get; set; }
        public List<string>? Tags { get; set; }
        public List<IngredientJson>? Ingredients { get; set; }
    }

    internal sealed class IngredientJson
    {
        public string? RawText { get; set; }
        public string? Name { get; set; }
        public double? Quantity { get; set; }
        public string? Unit { get; set; }
    }
}
