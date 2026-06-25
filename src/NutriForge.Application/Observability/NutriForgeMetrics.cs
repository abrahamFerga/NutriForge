using System.Diagnostics.Metrics;

namespace NutriForge.Application.Observability;

/// <summary>
/// Product telemetry for the SPEC success metrics (#50). One <see cref="Meter"/> ("NutriForge") whose
/// instruments map 1:1 onto the spec's KPIs, so the Aspire dashboard / alert rules can read them directly:
/// <list type="bullet">
///   <item>day-1 activation → <c>nutriforge.diary.entries_logged</c> (per new user, tagged by slot)</item>
///   <item>logging friction → <c>nutriforge.diary.time_to_log</c> + the <c>method</c> tag</item>
///   <item>plan correctness → <c>nutriforge.plan.generated</c> (VERIFY outcome) + <c>kcal_deviation_pct</c></item>
///   <item>loop completion → <c>nutriforge.shopping_list.created</c> (<c>from_plan</c>)</item>
///   <item>plan latency → <c>nutriforge.plan.generation_duration</c> (p95 SLO)</item>
/// </list>
/// Registered as a singleton and wired into OpenTelemetry via <c>AddMeter(MeterName)</c>.
/// </summary>
public sealed class NutriForgeMetrics : IDisposable
{
    public const string MeterName = "NutriForge";

    private readonly Meter _meter = new(MeterName, "1.0.0");
    private readonly Counter<long> _diaryEntries;
    private readonly Histogram<double> _timeToLog;
    private readonly Counter<long> _plansGenerated;
    private readonly Histogram<double> _planKcalDeviation;
    private readonly Histogram<double> _planDuration;
    private readonly Counter<long> _shoppingListsCreated;

    public NutriForgeMetrics()
    {
        _diaryEntries = _meter.CreateCounter<long>(
            "nutriforge.diary.entries_logged", unit: "{entry}",
            description: "Diary entries logged — the day-1 activation signal (tagged by meal slot and method).");
        _timeToLog = _meter.CreateHistogram<double>(
            "nutriforge.diary.time_to_log", unit: "s",
            description: "Client-reported seconds from opening the logger to a saved entry (logging friction).");
        _plansGenerated = _meter.CreateCounter<long>(
            "nutriforge.plan.generated", unit: "{plan}",
            description: "Diet plans generated, tagged with the VERIFY outcome (feasible, within_target, allergens_honored).");
        _planKcalDeviation = _meter.CreateHistogram<double>(
            "nutriforge.plan.kcal_deviation_pct", unit: "%",
            description: "Absolute % deviation of a generated plan from the calorie target (plan correctness).");
        _planDuration = _meter.CreateHistogram<double>(
            "nutriforge.plan.generation_duration", unit: "s",
            description: "End-to-end plan generation time (p95 latency SLO).");
        _shoppingListsCreated = _meter.CreateCounter<long>(
            "nutriforge.shopping_list.created", unit: "{list}",
            description: "Shopping lists created — the loop-completion signal (from_plan distinguishes plan→shop).");
    }

    /// <summary>Day-1 activation + logging friction. <paramref name="elapsedSeconds"/> feeds the histogram when the client reports it.</summary>
    public void DiaryEntryLogged(string mealSlot, string? method, double? elapsedSeconds)
    {
        var methodTag = new KeyValuePair<string, object?>("method", string.IsNullOrWhiteSpace(method) ? "unknown" : method);
        _diaryEntries.Add(1, new KeyValuePair<string, object?>("meal_slot", mealSlot), methodTag);
        if (elapsedSeconds is { } s && s >= 0)
        {
            _timeToLog.Record(s, methodTag);
        }
    }

    /// <summary>Plan correctness + latency, recorded once per generation against the VERIFY result.</summary>
    public void PlanGenerated(bool feasible, bool withinTarget, bool allergensHonored, double kcalDeviationPct, double durationSeconds)
    {
        _plansGenerated.Add(1,
            new KeyValuePair<string, object?>("feasible", feasible),
            new KeyValuePair<string, object?>("within_target", withinTarget),
            new KeyValuePair<string, object?>("allergens_honored", allergensHonored));
        _planKcalDeviation.Record(Math.Abs(kcalDeviationPct));
        _planDuration.Record(durationSeconds);
    }

    /// <summary>Loop completion — tag whether the list was built from an accepted plan.</summary>
    public void ShoppingListCreated(bool fromPlan) =>
        _shoppingListsCreated.Add(1, new KeyValuePair<string, object?>("from_plan", fromPlan));

    public void Dispose() => _meter.Dispose();
}
