using System.ComponentModel;
using Microsoft.Extensions.AI;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Food;
using NutriForge.Application.Tracking;
using NutriForge.Domain.Common;

namespace NutriForge.Infrastructure.Ai.Assistant;

/// <summary>One catalog hit the assistant can cite or offer to log.</summary>
public sealed record FoodHit(
    Guid Id, string Name, string? Brand, string Tier,
    double KcalPer100g, double ProteinPer100g, double FatPer100g, double CarbPer100g);

/// <summary>
/// The NutritionAssistant's tool surface. Each tool delegates to the same Application service the
/// REST API uses, so deterministic code owns every number (ADR-0004) — the LLM only decides which
/// tool to call and narrates the result. All tools are read-only and owner-scoped to the caller.
/// Resolved per request so they run inside the authenticated user's scope.
/// </summary>
public sealed class AssistantTools(
    FoodService foods,
    TargetService targets,
    DiaryService diary,
    ProfileService profiles,
    ICurrentUser user,
    IClock clock,
    LogProposalHolder proposals)
{
    private Guid Uid => user.UserId ?? throw new InvalidOperationException("No authenticated user.");

    /// <summary>The tools exposed to the agent for this request.</summary>
    public IList<AITool> ToolList() =>
    [
        AIFunctionFactory.Create(SearchFoods),
        AIFunctionFactory.Create(GetDailyTargets),
        AIFunctionFactory.Create(GetTodaySummary),
        AIFunctionFactory.Create(GetProfile),
        AIFunctionFactory.Create(ProposeLogFood),
    ];

    [Description("Search the food catalog by name. Returns matching foods with per-100g calories and macros and an id usable for logging.")]
    public async Task<IReadOnlyList<FoodHit>> SearchFoods(
        [Description("Food name or keywords, e.g. 'chicken breast' or 'banana'.")] string query,
        CancellationToken cancellationToken)
    {
        var results = await foods.SearchAsync(query, cancellationToken).ConfigureAwait(false);
        return results.Take(8)
            .Select(f => new FoodHit(f.Id, f.Name, f.Brand, f.VerificationStatus,
                f.KcalPer100g, f.ProteinPer100g, f.FatPer100g, f.CarbPer100g))
            .ToList();
    }

    [Description("Get the user's derived daily calorie and macro targets (kcal, protein, fat, carbs).")]
    public async Task<object> GetDailyTargets(CancellationToken cancellationToken)
    {
        var t = await targets.GetAsync(Uid, cancellationToken).ConfigureAwait(false);
        return t is null
            ? new { message = "No profile set yet — ask the user to complete their profile so targets can be computed." }
            : new { t.Kcal, t.ProteinG, t.FatG, t.CarbG, t.Formula };
    }

    [Description("Get today's calories and macros consumed so far and how much remains against the target.")]
    public async Task<object> GetTodaySummary(CancellationToken cancellationToken)
    {
        var day = await diary.GetDayAsync(Uid, clock.Today, cancellationToken).ConfigureAwait(false);
        return new
        {
            date = clock.Today,
            consumed = day.Consumed,
            target = day.Target,
            remaining = day.Remaining,
            entryCount = day.Entries.Count,
        };
    }

    [Description("Get the user's profile summary: goal, activity level, macro strategy, allergens and preferred diets.")]
    public async Task<object> GetProfile(CancellationToken cancellationToken)
    {
        var p = await profiles.GetAsync(Uid, cancellationToken).ConfigureAwait(false);
        return p is null
            ? new { message = "No profile set yet." }
            : new { goal = p.Goal.ToString(), activity = p.Activity.ToString(), macroStrategy = p.MacroStrategy.ToString(), p.Allergens, p.PreferredDiets };
    }

    [Description("Propose logging a food to today's diary for the user to confirm. This does NOT log it — it computes the entry (calories/macros) and returns a proposal; the user must confirm in the UI. Use a foodId and (optionally) a portionId from SearchFoods.")]
    public async Task<object> ProposeLogFood(
        [Description("The food id from SearchFoods.")] Guid foodId,
        [Description("Meal slot: Breakfast, Lunch, Dinner, or Snack.")] string mealSlot,
        [Description("How many portions (if a portionId is given) or grams (if not).")] double quantity,
        [Description("Optional portion id from SearchFoods; omit to log by grams.")] Guid? portionId,
        CancellationToken cancellationToken)
    {
        var slot = Enum.TryParse<MealSlot>(mealSlot, ignoreCase: true, out var parsed) ? parsed : MealSlot.Snack;
        var req = new AddDiaryEntryRequest(clock.Today, slot, foodId, portionId, quantity);
        var preview = await diary.PreviewAsync(Uid, req, cancellationToken).ConfigureAwait(false);

        proposals.Current = new LogProposal(
            foodId, preview.FoodName, portionId, preview.PortionName, quantity, preview.Grams,
            slot.ToString(), clock.Today, preview.Kcal, preview.ProteinG, preview.FatG, preview.CarbG);

        return new
        {
            proposed = true,
            preview.FoodName,
            grams = preview.Grams,
            kcal = preview.Kcal,
            mealSlot = slot.ToString(),
            note = "Proposal ready — tell the user exactly what will be logged and ask them to confirm.",
        };
    }
}
