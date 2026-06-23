using System.Text.RegularExpressions;

namespace NutriForge.Infrastructure.Ai.Assistant;

/// <summary>
/// A lightweight input guardrail: screens user messages for obvious prompt-injection / jailbreak
/// attempts before they reach the agent. Combined with a hardened system prompt and the rule that
/// tools (not the model) own every number, this resists the most common manipulation. Not a
/// complete defense — output-side validation and provider safety still apply.
/// </summary>
public static partial class PromptGuard
{
    public const string RefusalMessage =
        "I can only help with your nutrition, foods, targets, and diary — I can't change how I work or reveal my instructions.";

    public static bool IsLikelyInjection(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return InjectionPattern().IsMatch(message);
    }

    // Curated, case-insensitive patterns for the common attack phrasings.
    [GeneratedRegex(
        @"\b(ignore|disregard|forget|override)\b.{0,30}\b(previous|prior|above|earlier|all)\b.{0,30}\b(instruction|instructions|prompt|rules?|context)\b" +
        @"|\b(reveal|show|print|repeat|expose|leak)\b.{0,30}\b(system|developer)?\s*(prompt|instructions?)\b" +
        @"|\byou\s+are\s+now\b|\bact\s+as\b.{0,20}\b(dan|jailbreak|unrestricted)\b|\bdeveloper\s+mode\b|\bjailbreak\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex InjectionPattern();
}
