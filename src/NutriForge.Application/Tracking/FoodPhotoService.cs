using NutriForge.Application.Abstractions;
using NutriForge.Application.Food;
using NutriForge.Domain.Common;

namespace NutriForge.Application.Tracking;

/// <summary>
/// A food recognized in a photo, resolved either to a trusted catalog match (one-tap loggable via the
/// normal diary endpoint, nutrition from the log-time snapshot) or to a clearly-flagged AI estimate
/// when no catalog food matches. <c>Source</c> is <c>"catalog"</c> or <c>"estimate"</c>.
/// </summary>
public sealed record PhotoFoodCandidate(
    string Label,
    string Source,
    Guid? FoodId,
    Guid? PortionId,
    double Quantity,
    double Grams,
    double Kcal,
    double ProteinG,
    double FatG,
    double CarbG,
    double Confidence);

public sealed record PhotoAnalysisResult(IReadOnlyList<PhotoFoodCandidate> Items);

/// <summary>
/// Turns a meal photo into confirmable diary candidates: the vision model recognizes foods + rough
/// portions (<see cref="IFoodPhotoParser"/>), then deterministic code resolves each to a catalog food
/// (trusted numbers via <see cref="DiaryService.PreviewAsync"/>) or surfaces the model's estimate as a
/// flagged fallback. Nothing is logged until the user confirms through the normal diary endpoint.
/// </summary>
public sealed class FoodPhotoService(IFoodPhotoParser parser, FoodService foods, DiaryService diary)
{
    public bool IsConfigured => parser.IsConfigured;

    public async Task<PhotoAnalysisResult> AnalyzeAsync(
        Guid userId, ReadOnlyMemory<byte> image, string contentType, MealSlot mealSlot, DateOnly date,
        CancellationToken ct = default)
    {
        var guesses = await parser.AnalyzeAsync(image, contentType, ct).ConfigureAwait(false);
        var items = new List<PhotoFoodCandidate>();

        foreach (var g in guesses)
        {
            var hits = await foods.SearchAsync(g.Food, ct).ConfigureAwait(false);
            var food = hits.Count > 0 ? hits[0] : null;

            if (food is not null)
            {
                // Trusted: the catalog food at the estimated grams; nutrition from the log-time snapshot.
                var req = new AddDiaryEntryRequest(date, mealSlot, food.Id, null, g.EstimatedGrams);
                var preview = await diary.PreviewAsync(userId, req, ct).ConfigureAwait(false);
                items.Add(new PhotoFoodCandidate(
                    preview.FoodName, "catalog", food.Id, null,
                    g.EstimatedGrams, preview.Grams, preview.Kcal, preview.ProteinG, preview.FatG, preview.CarbG,
                    g.Confidence));
            }
            else
            {
                // Off-catalog: keep the model's estimate, clearly flagged (the only AI-owned number).
                items.Add(new PhotoFoodCandidate(
                    g.Food, "estimate", null, null,
                    g.EstimatedGrams, g.EstimatedGrams, g.EstimatedKcal, g.EstimatedProteinG, g.EstimatedFatG, g.EstimatedCarbG,
                    g.Confidence));
            }
        }

        return new PhotoAnalysisResult(items);
    }
}
