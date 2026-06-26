using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Tracking;
using NutriForge.Domain.Diary;
using NutriForge.Infrastructure.Persistence;

namespace NutriForge.Application.Tests;

/// <summary>Hydration tracking (#72) — accumulate a day's water, undo, set/carry a goal. Owner-scoped.</summary>
public sealed class HydrationTests
{
    private static readonly IClock Clock = new FakeClock(new DateTimeOffset(2026, 6, 25, 9, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Today = new(2026, 6, 25);

    private static NutriForgeDbContext Db(Guid userId, string name) =>
        new(new DbContextOptionsBuilder<NutriForgeDbContext>().UseInMemoryDatabase(name).Options, new FakeCurrentUser(userId));

    [Fact]
    public async Task Logging_accumulates_and_clamps_at_zero()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var svc = new HydrationService(db, Clock);

        Assert.Equal(250, (await svc.LogAsync(userId, new LogHydrationRequest(null, 250))).Ml);
        Assert.Equal(750, (await svc.LogAsync(userId, new LogHydrationRequest(null, 500))).Ml);

        // A negative amount undoes; the total never goes below zero.
        Assert.Equal(500, (await svc.LogAsync(userId, new LogHydrationRequest(null, -250))).Ml);
        var cleared = await svc.LogAsync(userId, new LogHydrationRequest(null, -5000));
        Assert.Equal(0, cleared.Ml);
    }

    [Fact]
    public async Task Get_day_defaults_to_zero_at_the_default_goal()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);

        var day = await new HydrationService(db, Clock).GetDayAsync(userId, null);

        Assert.Equal(0, day.Ml);
        Assert.Equal(HydrationDay.DefaultGoalMl, day.GoalMl);
    }

    [Fact]
    public async Task Setting_a_goal_clamps_and_carries_forward_to_new_days()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var svc = new HydrationService(db, Clock);

        // Over the max → clamped to 10 L.
        Assert.Equal(10_000, (await svc.SetGoalAsync(userId, new SetHydrationGoalRequest(99_999))).GoalMl);
        // A realistic goal sticks for today...
        Assert.Equal(3000, (await svc.SetGoalAsync(userId, new SetHydrationGoalRequest(3000))).GoalMl);

        // ...and a brand-new (future) day inherits the most recent goal rather than the default.
        var tomorrow = await svc.LogAsync(userId, new LogHydrationRequest(Today.AddDays(1), 200));
        Assert.Equal(3000, tomorrow.GoalMl);
    }

    [Fact]
    public async Task History_returns_recent_days_oldest_first()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var svc = new HydrationService(db, Clock);

        await svc.LogAsync(userId, new LogHydrationRequest(Today.AddDays(-2), 1000));
        await svc.LogAsync(userId, new LogHydrationRequest(Today, 2000));

        var history = await svc.HistoryAsync(userId, 30);

        Assert.Equal(2, history.Count);
        Assert.Equal(Today.AddDays(-2), history[0].Date);
        Assert.Equal(Today, history[1].Date);
    }

    [Fact]
    public async Task Hydration_is_owner_scoped()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var dbName = $"nf-{Guid.NewGuid()}";

        await using (var aliceDb = Db(alice, dbName))
        {
            await new HydrationService(aliceDb, Clock).LogAsync(alice, new LogHydrationRequest(null, 1500));
        }

        await using var bobDb = Db(bob, dbName);
        var bobDay = await new HydrationService(bobDb, Clock).GetDayAsync(bob, null);

        Assert.Equal(0, bobDay.Ml); // Bob sees none of Alice's water
    }
}
