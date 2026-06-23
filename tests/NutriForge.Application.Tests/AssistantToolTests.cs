using System.Text.Json;
using Microsoft.Extensions.AI;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Food;
using NutriForge.Application.Tracking;
using NutriForge.Domain.Catalog;
using NutriForge.Domain.Common;
using NutriForge.Infrastructure.Ai.Assistant;

namespace NutriForge.Application.Tests;

/// <summary>
/// Proves the agentic tool layer: the exact <see cref="AIFunction"/>s the NutritionAssistant agent
/// would invoke route through the real Application services and return code-owned numbers
/// (ADR-0004 — the LLM never owns a number). No live model needed; this is the deterministic half
/// the agent's tool loop dispatches.
/// </summary>
public sealed class AssistantToolTests
{
    private static readonly IClock Clock = new FakeClock(new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero));

    private sealed class NoOpCache : IFoodSearchCache
    {
        public Task<IReadOnlyList<FoodSummary>?> GetAsync(string q, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FoodSummary>?>(null);
        public Task SetAsync(string q, IReadOnlyList<FoodSummary> r, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private static async Task<AssistantTools> BuildToolsAsync(Guid userId, NutriForge.Infrastructure.Persistence.NutriForgeDbContext db)
    {
        var egg = new Domain.Catalog.Food
        {
            Name = "Egg, whole",
            Source = new FoodSource("seed", "egg"),
            VerificationStatus = VerificationStatus.Authoritative,
            NutrientProfile = new NutrientProfile { KcalPer100g = 143, ProteinPer100g = 12.6, FatPer100g = 9.5, CarbPer100g = 0.7 },
        };
        egg.AddPortion("1 large egg", 50);
        db.Foods.Add(egg);
        await db.SaveChangesAsync(CancellationToken.None);

        var targets = new TargetService(db, Clock);
        var profiles = new ProfileService(db, targets);
        await profiles.UpsertAsync(userId, new UpsertProfileRequest(
            Sex.Male, new DateOnly(1996, 6, 23), 180, 80, null,
            ActivityLevel.ModeratelyActive, Goal.ModerateCut, MacroStrategy.ProteinAnchored, null, null, null));

        var foods = new FoodService(db, new NoOpCache());
        var diary = new DiaryService(db, db, targets);
        return new AssistantTools(foods, targets, diary, profiles, new FakeCurrentUser(userId), Clock);
    }

    [Fact]
    public void Tool_surface_exposes_the_four_read_tools()
    {
        var tools = new AssistantTools(null!, null!, null!, null!, new FakeCurrentUser(Guid.NewGuid()), Clock);
        var names = tools.ToolList().OfType<AIFunction>().Select(f => f.Name).ToHashSet();

        Assert.Contains("SearchFoods", names);
        Assert.Contains("GetDailyTargets", names);
        Assert.Contains("GetTodaySummary", names);
        Assert.Contains("GetProfile", names);
    }

    [Fact]
    public async Task GetDailyTargets_tool_returns_the_code_owned_target()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var tools = await BuildToolsAsync(userId, db);

        var fn = tools.ToolList().OfType<AIFunction>().First(f => f.Name == "GetDailyTargets");
        var result = await fn.InvokeAsync(cancellationToken: CancellationToken.None);

        // The agent gets exactly the deterministic number — 2345 kcal — not an LLM guess.
        Assert.Contains("2345", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task SearchFoods_tool_returns_real_catalog_hits()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var tools = await BuildToolsAsync(userId, db);

        var fn = tools.ToolList().OfType<AIFunction>().First(f => f.Name == "SearchFoods");
        var result = await fn.InvokeAsync(new AIFunctionArguments { ["query"] = "egg" }, CancellationToken.None);

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("Egg", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("143", json); // the seeded per-100g kcal
    }
}
