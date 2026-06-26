using NutriForge.Domain.Common;
using NutriForge.Domain.Recipes;

namespace NutriForge.Application.DietGen;

/// <summary>
/// One agent-chosen meal: a recipe placed at a (block, slot). The structured output is deliberately
/// just recipe ids + positions — the agent <em>selects</em>, it never returns a calorie or serving
/// number. Quantities are set deterministically afterwards (REPAIR), preserving the pipeline invariant
/// that the LLM never owns a number.
/// </summary>
public sealed record MealSelection(int Block, MealSlot Meal, Guid RecipeId);

/// <summary>
/// SELECT (the soft-objective step). An MAF agent composes a plan from the already hard-filtered pool,
/// optimizing variety, no-repeat, cuisine balance, and prep-batching. It is advisory: the generator
/// re-checks every pick against the pool and runs the same VERIFY → REPAIR → allergen re-check, so a
/// missing/garbage/unsafe proposal can only ever fall back to the deterministic greedy selection — it
/// can never weaken a guarantee.
/// </summary>
public interface IMealSelectAgent
{
    /// <summary>Whether an LLM provider is configured; false ⇒ the generator uses greedy selection.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Propose a recipe for each of the <paramref name="numBlocks"/> × <paramref name="mealsPerDay"/>
    /// slots, drawn from <paramref name="pool"/>. Return null (or an unusable proposal) to defer to the
    /// deterministic selection.
    /// </summary>
    Task<IReadOnlyList<MealSelection>?> SelectAsync(
        IReadOnlyList<Recipe> pool, DietIntent intent, int numBlocks, int mealsPerDay, CancellationToken ct = default);
}
