namespace NutriForge.Application.Abstractions;

/// <summary>
/// One food a vision model recognized in a meal photo, with a rough portion + nutrition guess.
/// </summary>
public sealed record PhotoFoodGuess(
    string Food,
    double EstimatedGrams,
    double EstimatedKcal,
    double EstimatedProteinG,
    double EstimatedFatG,
    double EstimatedCarbG,
    double Confidence);

/// <summary>
/// Recognizes the foods in a meal photo. The vision model does ONLY recognition plus a rough
/// portion/calorie guess; deterministic code then resolves each item to a catalog food (which owns
/// the trusted numbers), and the guess is only a clearly-flagged fallback for off-catalog foods
/// (ADR-0004). Provider-swappable; a null/unconfigured implementation turns the feature off (503).
/// </summary>
public interface IFoodPhotoParser
{
    bool IsConfigured { get; }

    Task<IReadOnlyList<PhotoFoodGuess>> AnalyzeAsync(
        ReadOnlyMemory<byte> image, string contentType, CancellationToken ct = default);
}
