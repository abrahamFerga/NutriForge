namespace NutriForge.Application.Abstractions;

/// <summary>One food the parser extracted from free text, before resolution against the catalog.</summary>
public sealed record ParsedFoodItem(string Food, double Quantity, string? Unit);

/// <summary>
/// Parses a free-text meal description ("2 eggs and toast") into structured food items. The LLM
/// does ONLY the language understanding here; deterministic code resolves each item to a catalog
/// food and owns every nutrition number (ADR-0004). Provider-swappable; null impl ⇒ feature off.
/// </summary>
public interface INlDiaryParser
{
    bool IsConfigured { get; }

    Task<IReadOnlyList<ParsedFoodItem>> ParseAsync(string text, CancellationToken ct = default);
}
