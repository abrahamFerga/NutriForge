using NutriForge.Application.Abstractions;
using NutriForge.Application.Food;
using NutriForge.Domain.Common;

namespace NutriForge.Application.Tracking;

/// <summary>A resolved, confirmable diary entry candidate from a natural-language parse (not logged).</summary>
public sealed record ParseCandidate(
    DateOnly Date,
    string MealSlot,
    Guid FoodId,
    string FoodName,
    Guid? PortionId,
    string PortionName,
    double Quantity,
    double Grams,
    double Kcal,
    double ProteinG,
    double FatG,
    double CarbG);

/// <summary>The result of parsing a meal description: confirmable candidates + items we couldn't match.</summary>
public sealed record DiaryParseResult(IReadOnlyList<ParseCandidate> Candidates, IReadOnlyList<string> Unresolved);

/// <summary>
/// Turns "2 eggs and toast" into confirmable diary candidates: the LLM extracts the food items
/// (<see cref="INlDiaryParser"/>), then deterministic code resolves each to a catalog food and
/// computes its snapshot via <see cref="DiaryService.PreviewAsync"/> — nothing is logged until the
/// user confirms (through the normal diary endpoint), and the model never owns a number.
/// </summary>
public sealed class DiaryParseService(INlDiaryParser parser, FoodService foods, DiaryService diary, IClock clock)
{
    public bool IsConfigured => parser.IsConfigured;

    public async Task<DiaryParseResult> ParseAsync(
        Guid userId, string text, MealSlot mealSlot, DateOnly? date, CancellationToken ct = default)
    {
        var day = date ?? clock.Today;
        var items = await parser.ParseAsync(text, ct).ConfigureAwait(false);

        var candidates = new List<ParseCandidate>();
        var unresolved = new List<string>();

        foreach (var item in items)
        {
            var hits = await foods.SearchAsync(item.Food, ct).ConfigureAwait(false);
            var food = hits.Count > 0 ? hits[0] : null;
            if (food is null)
            {
                unresolved.Add(item.Food);
                continue;
            }

            Guid? portionId = null;
            if (food.Portions.Count > 0 && !IsGramsUnit(item.Unit))
            {
                portionId = food.Portions[0].Id;
            }

            var quantity = item.Quantity <= 0 ? 1 : item.Quantity;
            var req = new AddDiaryEntryRequest(day, mealSlot, food.Id, portionId, quantity);
            var preview = await diary.PreviewAsync(userId, req, ct).ConfigureAwait(false);

            candidates.Add(new ParseCandidate(
                day, mealSlot.ToString(), food.Id, preview.FoodName, portionId, preview.PortionName,
                quantity, preview.Grams, preview.Kcal, preview.ProteinG, preview.FatG, preview.CarbG));
        }

        return new DiaryParseResult(candidates, unresolved);
    }

    private static bool IsGramsUnit(string? unit) =>
        unit is not null && unit.Trim().ToLowerInvariant() is "g" or "gram" or "grams" or "gr";
}
