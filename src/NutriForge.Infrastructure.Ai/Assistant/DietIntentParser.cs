using Microsoft.Agents.AI;
using NutriForge.Application.Abstractions;
using NutriForge.Application.DietGen;

namespace NutriForge.Infrastructure.Ai.Assistant;

/// <summary>
/// PARSE — turns a free-text diet desire into a structured <see cref="DietIntent"/> via the LLM
/// (structured output). It only fills intent fields; allergens/dislikes it finds are merged into the
/// deterministic exclusion set, which FILTER and VERIFY still enforce. Missing fields keep defaults.
/// </summary>
public sealed class DietIntentParser(IAssistantAgentFactory factory) : IDietIntentParser
{
    private const string Prompt =
        """
        Extract a structured diet intent from the user's desire. Return:
        - kcalTarget: a number if the user states a calorie goal, else null
        - dietType: a single lower-case slug if a diet is named (e.g. "vegan", "vegetarian",
          "high-protein", "keto"), else null
        - dislikes: foods/ingredients to avoid that the user mentions (e.g. "mushroom"), else empty
        - maxPrepMinutes: a number if the user constrains cook/prep time, else null
        - mealsPerDay: a number if stated, else null
        - horizonDays: a number of days if stated, else null
        Do NOT compute nutrition. Only extract what the user said.
        """;

    public bool IsConfigured => factory.IsConfigured;

    public async Task<DietIntent> ParseAsync(string desire, DietIntent defaults, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        if (!factory.IsConfigured || string.IsNullOrWhiteSpace(desire))
        {
            return defaults;
        }

        var agent = factory.CreateBareAgent(Prompt);
        var parsed = (await agent.RunAsync<ParsedIntent>(desire, cancellationToken: ct).ConfigureAwait(false)).Result;
        if (parsed is null)
        {
            return defaults;
        }

        var exclude = new HashSet<string>(defaults.ExcludeKeywords, StringComparer.OrdinalIgnoreCase);
        foreach (var d in parsed.Dislikes ?? [])
        {
            if (!string.IsNullOrWhiteSpace(d))
            {
                exclude.Add(d.Trim());
            }
        }

        return defaults with
        {
            KcalTarget = parsed.KcalTarget ?? defaults.KcalTarget,
            DietSlug = string.IsNullOrWhiteSpace(parsed.DietType) ? defaults.DietSlug : parsed.DietType.Trim().ToLowerInvariant(),
            ExcludeKeywords = [.. exclude],
            MaxPrepMinutes = parsed.MaxPrepMinutes ?? defaults.MaxPrepMinutes,
            MealsPerDay = parsed.MealsPerDay ?? defaults.MealsPerDay,
            HorizonDays = parsed.HorizonDays ?? defaults.HorizonDays,
        };
    }

    internal sealed class ParsedIntent
    {
        public double? KcalTarget { get; set; }
        public string? DietType { get; set; }
        public List<string>? Dislikes { get; set; }
        public int? MaxPrepMinutes { get; set; }
        public int? MealsPerDay { get; set; }
        public int? HorizonDays { get; set; }
    }
}
