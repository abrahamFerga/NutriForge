using System.Globalization;
using System.Text;
using Microsoft.Agents.AI;
using NutriForge.Application.DietGen;
using NutriForge.Domain.Common;
using NutriForge.Domain.Recipes;

namespace NutriForge.Infrastructure.Ai.Assistant;

/// <summary>
/// SELECT (#36) — an MAF agent composes the meal selection from the already hard-filtered pool,
/// optimizing soft objectives (variety, no-repeat, cuisine balance, prep-batching). Its structured
/// output is recipe ids per slot only — it cannot return a calorie or serving number. The generator
/// validates every pick against the pool and runs the same VERIFY → REPAIR → allergen re-check, so a
/// bad or unsafe proposal can only ever fall back to greedy; this stays purely advisory.
/// </summary>
public sealed class MealSelectAgent(IAssistantAgentFactory factory) : IMealSelectAgent
{
    private static readonly MealSlot[] AllMeals = [MealSlot.Breakfast, MealSlot.Lunch, MealSlot.Dinner, MealSlot.Snack];

    private const string Prompt =
        """
        You compose a meal plan. You are given a POOL of pre-approved recipes (already filtered for the
        user's diet and allergens) and a GRID of slots to fill (blocks × meals). Choose exactly one
        recipe from the pool for every slot.

        MEAL TYPE IS A HARD RULE: every slot has a meal type (Breakfast, Lunch, Dinner, Snack) and every
        recipe lists its meal type in its tags. You MUST fill a slot only with a recipe whose tags
        include that slot's meal type — a Breakfast slot may take ONLY a recipe tagged "breakfast", a
        Snack slot ONLY a recipe tagged "snack", and so on. A recipe with no meal-type tag may go in any
        slot. Never place a dinner/lunch dish in a breakfast slot.

        Then optimize for: variety (avoid repeating the same recipe across slots), cuisine balance, and
        batch-friendliness (a block is cooked once and eaten for several days, so prefer recipes that
        keep well within a block). Reuse a recipe only when that meal type's recipes are too few to avoid it.

        Return, for each slot, its block number, the meal name, and the chosen recipe id. Choose ONLY
        from the given recipe ids. Do NOT output any calorie, macro, or serving numbers — you only pick
        recipes; the app computes every quantity.
        """;

    public bool IsConfigured => factory.IsConfigured;

    public async Task<IReadOnlyList<MealSelection>?> SelectAsync(
        IReadOnlyList<Recipe> pool, DietIntent intent, int numBlocks, int mealsPerDay, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pool);
        if (!factory.IsConfigured || pool.Count == 0 || numBlocks <= 0 || mealsPerDay <= 0)
        {
            return null;
        }

        var agent = factory.CreateBareAgent(Prompt);
        var response = await agent.RunAsync<AgentSelection>(
            BuildInput(pool, numBlocks, mealsPerDay), cancellationToken: ct).ConfigureAwait(false);
        if (response.Result?.Slots is not { Count: > 0 } proposed)
        {
            return null;
        }

        var meals = AllMeals.Take(mealsPerDay).ToArray();
        var picks = new List<MealSelection>(proposed.Count);
        foreach (var s in proposed)
        {
            if (!Guid.TryParse(s.RecipeId, out var id))
            {
                continue; // not a recipe id ⇒ drop; the generator will fall back if the grid ends up incomplete
            }

            var meal = meals.Cast<MealSlot?>()
                .FirstOrDefault(m => string.Equals(m!.Value.ToString(), s.Meal?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (meal is null)
            {
                continue;
            }

            picks.Add(new MealSelection(s.Block, meal.Value, id));
        }

        return picks.Count > 0 ? picks : null;
    }

    private static string BuildInput(IReadOnlyList<Recipe> pool, int numBlocks, int mealsPerDay)
    {
        var meals = AllMeals.Take(mealsPerDay).Select(m => m.ToString());
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"GRID: blocks 1..{numBlocks}; meals per block: {string.Join(", ", meals)}.");
        sb.AppendLine("POOL (id | name | kcal/serving | tags):");
        foreach (var r in pool)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"- {r.Id} | {r.Name} | {r.KcalPerServing:0} | {string.Join(",", r.Tags)}");
        }

        return sb.ToString();
    }

    internal sealed class AgentSelection
    {
        public List<AgentPick>? Slots { get; set; }
    }

    internal sealed class AgentPick
    {
        public int Block { get; set; }
        public string? Meal { get; set; }
        public string? RecipeId { get; set; }
    }
}
