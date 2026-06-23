using Microsoft.Agents.AI;
using NutriForge.Application.Abstractions;

namespace NutriForge.Infrastructure.Ai.Assistant;

/// <summary>
/// Single-shot structured-output parse of a meal description into food items. The LLM does only the
/// language understanding (it returns names + quantities, never nutrition); the Application layer
/// resolves each item to a catalog food and owns the numbers (ADR-0004).
/// </summary>
public sealed class NlDiaryParser(IAssistantAgentFactory factory) : INlDiaryParser
{
    private const string ParsePrompt =
        """
        Extract the individual foods from the user's meal description as structured items.
        For each food return: the food name (singular, plain, e.g. "egg", "white rice"), the
        quantity as a number (default 1 if unstated), and the unit if the user stated one
        (e.g. "g", "cup", "slice", "tbsp") or null. Do NOT compute or guess any nutrition numbers.
        Ignore non-food words. If the text contains no food, return an empty list.
        """;

    public bool IsConfigured => factory.IsConfigured;

    public async Task<IReadOnlyList<ParsedFoodItem>> ParseAsync(string text, CancellationToken ct = default)
    {
        if (!factory.IsConfigured || string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var agent = factory.CreateBareAgent(ParsePrompt);
        var response = await agent.RunAsync<ParsedDiary>(text, cancellationToken: ct).ConfigureAwait(false);

        return response.Result?.Items?
            .Where(i => !string.IsNullOrWhiteSpace(i.Food))
            .Select(i => new ParsedFoodItem(i.Food!.Trim(), i.Quantity <= 0 ? 1 : i.Quantity, i.Unit))
            .ToList() ?? [];
    }

    // Structured-output target — settable members so MAF can deserialize the model's JSON.
    internal sealed class ParsedDiary
    {
        public List<ParsedItem> Items { get; set; } = [];
    }

    internal sealed class ParsedItem
    {
        public string? Food { get; set; }
        public double Quantity { get; set; }
        public string? Unit { get; set; }
    }
}
