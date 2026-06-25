using System.Diagnostics.Metrics;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Observability;
using NutriForge.Application.Tracking;
using NutriForge.Domain.Catalog;
using NutriForge.Domain.Common;

namespace NutriForge.Application.Tests;

/// <summary>Product-KPI telemetry (#50) — proves the SPEC success metrics are emitted with the right tags.</summary>
public sealed class MetricsTests
{
    private static readonly IClock Clock = new FakeClock(new DateTimeOffset(2026, 6, 25, 9, 0, 0, TimeSpan.Zero));

    /// <summary>Captures every measurement from the "NutriForge" meter for assertions.</summary>
    private sealed class Capture : IDisposable
    {
        private readonly MeterListener _listener = new();
        public List<(string Name, double Value, Dictionary<string, object?> Tags)> Items { get; } = [];

        public Capture()
        {
            _listener.InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name == NutriForgeMetrics.MeterName)
                {
                    l.EnableMeasurementEvents(inst);
                }
            };
            _listener.SetMeasurementEventCallback<long>((inst, val, tags, _) => Record(inst, val, tags));
            _listener.SetMeasurementEventCallback<double>((inst, val, tags, _) => Record(inst, val, tags));
            _listener.Start();
        }

        private void Record(Instrument inst, double val, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var t in tags)
            {
                dict[t.Key] = t.Value;
            }

            Items.Add((inst.Name, val, dict));
        }

        public (string Name, double Value, Dictionary<string, object?> Tags) Single(string name) =>
            Items.Single(i => i.Name == name);

        public void Dispose() => _listener.Dispose();
    }

    [Fact]
    public async Task Diary_log_emits_activation_and_time_to_log_with_method_tag()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var egg = new Domain.Catalog.Food
        {
            Name = "Egg",
            Source = new FoodSource("seed", "egg"),
            VerificationStatus = VerificationStatus.Authoritative,
            NutrientProfile = new NutrientProfile { KcalPer100g = 143, ProteinPer100g = 12.6, FatPer100g = 9.5, CarbPer100g = 0.7 },
        };
        egg.AddPortion("1 large", 50);
        db.Foods.Add(egg);
        await db.SaveChangesAsync(CancellationToken.None);

        using var metrics = new NutriForgeMetrics();
        using var capture = new Capture();
        var diary = new DiaryService(db, db, new TargetService(db, Clock), metrics);

        await diary.AddAsync(userId, new AddDiaryEntryRequest(
            Clock.Today, MealSlot.Breakfast, egg.Id, egg.Portions.First().Id, 2, Method: "search", ElapsedSeconds: 8.0));

        var logged = capture.Single("nutriforge.diary.entries_logged");
        Assert.Equal(1, logged.Value);
        Assert.Equal("Breakfast", logged.Tags["meal_slot"]);
        Assert.Equal("search", logged.Tags["method"]);

        var ttl = capture.Single("nutriforge.diary.time_to_log");
        Assert.Equal(8.0, ttl.Value);
        Assert.Equal("search", ttl.Tags["method"]);
    }

    [Fact]
    public void Diary_log_without_a_client_time_records_no_time_to_log()
    {
        using var metrics = new NutriForgeMetrics();
        using var capture = new Capture();

        metrics.DiaryEntryLogged("Lunch", method: null, elapsedSeconds: null);

        var logged = capture.Single("nutriforge.diary.entries_logged");
        Assert.Equal("unknown", logged.Tags["method"]); // defaults, never blank
        Assert.DoesNotContain(capture.Items, i => i.Name == "nutriforge.diary.time_to_log");
    }

    [Fact]
    public void Plan_generated_metric_carries_the_verify_outcome_and_latency()
    {
        using var metrics = new NutriForgeMetrics();
        using var capture = new Capture();

        metrics.PlanGenerated(feasible: true, withinTarget: true, allergensHonored: true, kcalDeviationPct: -2.5, durationSeconds: 0.42);

        var generated = capture.Single("nutriforge.plan.generated");
        Assert.Equal(1, generated.Value);
        Assert.Equal(true, generated.Tags["feasible"]);
        Assert.Equal(true, generated.Tags["within_target"]);
        Assert.Equal(true, generated.Tags["allergens_honored"]);

        Assert.Equal(2.5, capture.Single("nutriforge.plan.kcal_deviation_pct").Value); // absolute
        Assert.Equal(0.42, capture.Single("nutriforge.plan.generation_duration").Value);
    }

    [Fact]
    public void Shopping_list_metric_tags_whether_it_came_from_a_plan()
    {
        using var metrics = new NutriForgeMetrics();
        using var capture = new Capture();

        metrics.ShoppingListCreated(fromPlan: true);

        var created = capture.Single("nutriforge.shopping_list.created");
        Assert.Equal(1, created.Value);
        Assert.Equal(true, created.Tags["from_plan"]);
    }
}
