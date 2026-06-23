using NutriForge.Application.DietGen;

namespace NutriForge.Application.Abstractions;

/// <summary>
/// PARSE (step 1) — turns a free-text desire into a structured <see cref="DietIntent"/> via the LLM
/// (structured output). Missing fields fall back to the supplied defaults. The LLM never owns
/// the numbers downstream; allergens are still enforced deterministically in FILTER and VERIFY.
/// </summary>
public interface IDietIntentParser
{
    bool IsConfigured { get; }

    Task<DietIntent> ParseAsync(string desire, DietIntent defaults, CancellationToken ct = default);
}
