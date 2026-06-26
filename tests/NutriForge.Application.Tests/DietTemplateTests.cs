using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Application.DietGen;
using NutriForge.Application.Planning;
using NutriForge.Application.Tracking;
using NutriForge.Infrastructure.DietGen;
using NutriForge.Infrastructure.Persistence;

namespace NutriForge.Application.Tests;

/// <summary>
/// Saved diet presets (#102): named generation parameters re-run in one tap. Composes with the saved
/// household (#100) — a one-tap regenerate attaches the saved household automatically.
/// </summary>
public sealed class DietTemplateTests
{
    private static readonly IClock Clock = new FakeClock(new DateTimeOffset(2026, 6, 26, 12, 0, 0, TimeSpan.Zero));

    private sealed class NoOpIntentParser : IDietIntentParser
    {
        public bool IsConfigured => false;
        public Task<DietIntent> ParseAsync(string desire, DietIntent defaults, CancellationToken ct = default) =>
            Task.FromResult(defaults);
    }

    private static DietPlanService BuildPlanService(NutriForgeDbContext db)
    {
        var targets = new TargetService(db, Clock);
        var profiles = new ProfileService(db, targets);
        var generator = new DietPlanGenerator(new OrToolsPortionOptimizer());
        var shopping = new ShoppingListService(db, db);
        return new DietPlanService(db, db, targets, profiles, new NoOpIntentParser(), generator, shopping, Clock);
    }

    [Fact]
    public async Task Template_crud_round_trips_and_clamps()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var svc = new DietTemplateService(db);

        var cut = await svc.AddAsync(userId, new UpsertDietTemplateRequest(
            "My usual cut", "high-protein", 1900, 30, 3, HorizonDays: 6, BlockSize: 99, Desire: "lots of fish"));

        Assert.NotNull(cut);
        Assert.Equal("high-protein", cut!.DietSlug);
        Assert.Equal(6, cut.HorizonDays);
        Assert.Equal(6, cut.BlockSize); // clamped to horizon

        var updated = await svc.UpdateAsync(userId, cut.Id, new UpsertDietTemplateRequest(
            "My usual cut", "high-protein", 2000, 30, 3, 6, 2, "lots of fish"));
        Assert.Equal(2000, updated!.KcalTarget);
        Assert.Equal(2, updated.BlockSize);

        Assert.Single(await svc.ListAsync(userId));
        Assert.True(await svc.DeleteAsync(userId, cut.Id));
        Assert.Empty(await svc.ListAsync(userId));
    }

    [Fact]
    public async Task ToCreateRequest_maps_saved_params_and_leaves_members_null_for_household_fallback()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var svc = new DietTemplateService(db);

        var t = await svc.AddAsync(userId, new UpsertDietTemplateRequest(
            "Family week", "vegetarian", 2200, 40, 3, 7, 3, null));

        var req = await svc.ToCreateRequestAsync(userId, t!.Id);

        Assert.NotNull(req);
        Assert.Equal("vegetarian", req!.DietSlug);
        Assert.Equal(2200, req.KcalTarget);
        Assert.Equal(7, req.HorizonDays);
        Assert.Equal(3, req.BlockSize);
        Assert.Null(req.Members); // null ⇒ DietPlanService attaches the saved household
    }

    [Fact]
    public async Task One_tap_generate_from_template_attaches_the_saved_household()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        await DevDataSeeder.SeedAsync(db, CancellationToken.None);

        await new HouseholdService(db).AddAsync(userId, new UpsertHouseholdMemberRequest("Fiancée", "Partner", 1600));

        var templates = new DietTemplateService(db);
        var t = await templates.AddAsync(userId, new UpsertDietTemplateRequest(
            "Quick week", null, 2000, null, 3, 3, 1, null));

        // The endpoint's one-tap path: build the saved request, then generate.
        var req = await templates.ToCreateRequestAsync(userId, t!.Id);
        var dto = await BuildPlanService(db).CreateAsync(userId, req!);

        var names = dto.Members.Select(m => m.Name).ToList();
        Assert.Contains("You", names);
        Assert.Contains("Fiancée", names);
    }
}
