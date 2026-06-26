using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Application.DietGen;
using NutriForge.Application.Planning;
using NutriForge.Application.Tracking;
using NutriForge.Infrastructure.DietGen;
using NutriForge.Infrastructure.Persistence;

namespace NutriForge.Application.Tests;

/// <summary>
/// Saved household (#100): the people a user regularly cooks for are stored once and auto-attached to
/// every diet plan that doesn't override them — so they never re-add their partner to each new plan.
/// </summary>
public sealed class HouseholdTests
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

    private static CreateDietPlanRequest PlanRequest(IReadOnlyList<PlanMemberInput>? members) => new(
        Desire: null, KcalTarget: 2000, DietSlug: null, ExcludeAllergens: null, Dislikes: null,
        MaxPrepMinutes: null, MealsPerDay: 3, HorizonDays: 3, Eaters: null, BlockSize: null, Members: members);

    [Fact]
    public async Task Household_crud_round_trips_oldest_first()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var household = new HouseholdService(db);

        var fiancee = await household.AddAsync(userId, new UpsertHouseholdMemberRequest("Fiancée", "Partner", 1600));
        var sam = await household.AddAsync(userId, new UpsertHouseholdMemberRequest("Sam", null, null));

        Assert.NotNull(fiancee);
        Assert.Equal(1600, fiancee!.TargetKcal);
        Assert.Null(sam!.TargetKcal); // "same as me"

        var list = await household.ListAsync(userId);
        Assert.Equal(2, list.Count);
        Assert.Equal("Fiancée", list[0].Name); // oldest-first ordering is stable

        var updated = await household.UpdateAsync(userId, fiancee.Id, new UpsertHouseholdMemberRequest("Fiancée", "Partner", 1700));
        Assert.Equal(1700, updated!.TargetKcal);

        Assert.True(await household.DeleteAsync(userId, sam.Id));
        Assert.Single(await household.ListAsync(userId));
    }

    [Fact]
    public async Task Adding_past_the_cap_is_rejected()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var household = new HouseholdService(db);

        for (var i = 0; i < 12; i++)
        {
            Assert.NotNull(await household.AddAsync(userId, new UpsertHouseholdMemberRequest($"M{i}", null, null)));
        }

        Assert.Null(await household.AddAsync(userId, new UpsertHouseholdMemberRequest("overflow", null, null)));
    }

    [Fact]
    public async Task Saved_household_is_auto_attached_when_a_plan_omits_members()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        await DevDataSeeder.SeedAsync(db, CancellationToken.None);

        var household = new HouseholdService(db);
        await household.AddAsync(userId, new UpsertHouseholdMemberRequest("Fiancée", "Partner", 1600));

        // Members omitted (null) → the saved household is attached automatically (owner + Fiancée).
        var dto = await BuildPlanService(db).CreateAsync(userId, PlanRequest(members: null));

        var names = dto.Members.Select(m => m.Name).ToList();
        Assert.Equal(2, dto.Members.Count);
        Assert.Contains("You", names);
        Assert.Contains("Fiancée", names);
    }

    [Fact]
    public async Task Explicit_empty_members_overrides_the_saved_household()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        await DevDataSeeder.SeedAsync(db, CancellationToken.None);

        var household = new HouseholdService(db);
        await household.AddAsync(userId, new UpsertHouseholdMemberRequest("Fiancée", "Partner", 1600));

        // Members explicitly empty → cooking for just me; the saved household must NOT be attached.
        var dto = await BuildPlanService(db).CreateAsync(userId, PlanRequest(members: []));

        Assert.Empty(dto.Members);
    }
}
