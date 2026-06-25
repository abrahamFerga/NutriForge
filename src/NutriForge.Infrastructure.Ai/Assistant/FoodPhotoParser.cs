using Microsoft.Extensions.AI;
using NutriForge.Application.Abstractions;

namespace NutriForge.Infrastructure.Ai.Assistant;

/// <summary>
/// Vision parse of a meal photo into recognized foods. The model returns names + a rough portion and
/// calorie estimate; the Application layer resolves each to a catalog food and owns the trusted numbers
/// (ADR-0004). Same provider/agent factory as the text assistant — unconfigured ⇒ feature off.
/// </summary>
public sealed class FoodPhotoParser(IAssistantAgentFactory factory) : IFoodPhotoParser
{
    private const string Prompt =
        """
        You are a nutrition vision assistant. Identify each distinct food in the photo. For each food
        return: a plain singular name (e.g. "grilled chicken breast", "white rice"), your best estimate
        of the EDIBLE portion in grams, and a rough estimate of its calories and protein/fat/carb grams
        for that portion, plus a confidence from 0 to 1. Estimate conservatively from visible portion
        size. Ignore plates, cutlery, and non-food. If there is no food, return an empty list.
        """;

    public bool IsConfigured => factory.IsConfigured;

    public async Task<IReadOnlyList<PhotoFoodGuess>> AnalyzeAsync(
        ReadOnlyMemory<byte> image, string contentType, CancellationToken ct = default)
    {
        if (!factory.IsConfigured || image.IsEmpty)
        {
            return [];
        }

        var agent = factory.CreateBareAgent(Prompt);
        var message = new ChatMessage(ChatRole.User,
        [
            new TextContent("Identify the foods and estimate portion grams + calories + macros."),
            new DataContent(image, contentType),
        ]);

        var response = await agent.RunAsync<VisionFoods>(message, cancellationToken: ct).ConfigureAwait(false);

        return response.Result?.Items?
            .Where(i => !string.IsNullOrWhiteSpace(i.Food))
            .Select(i => new PhotoFoodGuess(
                i.Food!.Trim(),
                i.Grams <= 0 ? 100 : i.Grams,
                Math.Max(0, i.Kcal),
                Math.Max(0, i.ProteinG),
                Math.Max(0, i.FatG),
                Math.Max(0, i.CarbG),
                Math.Clamp(i.Confidence, 0, 1)))
            .ToList() ?? [];
    }

    // Structured-output target — settable members so MAF can deserialize the model's JSON.
    internal sealed class VisionFoods
    {
        public List<VisionFood> Items { get; set; } = [];
    }

    internal sealed class VisionFood
    {
        public string? Food { get; set; }
        public double Grams { get; set; }
        public double Kcal { get; set; }
        public double ProteinG { get; set; }
        public double FatG { get; set; }
        public double CarbG { get; set; }
        public double Confidence { get; set; }
    }
}
