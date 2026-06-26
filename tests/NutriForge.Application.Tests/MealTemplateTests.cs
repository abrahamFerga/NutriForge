using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Tracking;
using NutriForge.Domain.Catalog;
using NutriForge.Domain.Common;
using NutriForge.Infrastructure.Persistence;

namespace NutriForge.Application.Tests;

/// <summary>Meal templates (#70) — save a combination of foods and log it in one tap. Owner-scoped.</summary>
public sealed class MealTemplateTests
{
    private static readonly IClock Clock = new FakeClock(new DateTimeOffset(2026, 6, 25, 8, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Today = new(2026, 6, 25);

    private static NutriForgeDbContext Db(Guid userId, string name) =>
        new(new DbContextOptionsBuilder<NutriForgeDbContext>().UseInMemoryDatabase(name).Options, new FakeCurrentUser(userId));

    private static MealTemplateService Service(NutriForgeDbContext db) =>
        new(db, db, new DiaryService(db, db, new TargetService(db, Clock)));

    private static Domain.Catalog.Food SeedFood(NutriForgeDbContext db, string name, double kcal, params (string Name, double Grams)[] portions)
    {
        var food = new Domain.Catalog.Food
        {
            Name = name,
            Source = new FoodSource("seed", name),
            VerificationStatus = VerificationStatus.Authoritative,
            NutrientProfile = new NutrientProfile { KcalPer100g = kcal, ProteinPer100g = 10, FatPer100g = 5, CarbPer100g = 20 },
        };
        foreach (var (pName, grams) in portions)
        {
            food.AddPortion(pName, grams);
        }

        db.Foods.Add(food);
        return food;
    }

    [Fact]
    public async Task Create_list_and_log_a_saved_meal()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var egg = SeedFood(db, "Egg", 143, ("1 large egg", 50));
        var oats = SeedFood(db, "Oats", 380); // logged in grams
        await db.SaveChangesAsync(CancellationToken.None);
        var svc = Service(db);

        var created = await svc.CreateAsync(userId, "Usual breakfast",
        [
            new MealTemplateItemInput(egg.Id, egg.Portions.First().Id, 2), // 2 × 50 g = 100 g → 143 kcal
            new MealTemplateItemInput(oats.Id, null, 80),                  // 80 g → 304 kcal
        ]);

        Assert.NotNull(created);
        Assert.Equal(2, created!.Items.Count);
        Assert.Equal(143 + 304, created.Kcal, 1); // rolled-up total

        var list = await svc.ListAsync(userId);
        Assert.Single(list);

        // Log it into today's breakfast → two diary entries, totalling the template's kcal.
        var logged = await svc.LogAsync(userId, created.Id, Today, MealSlot.Breakfast);
        Assert.Equal(2, logged);
        var day = await new DiaryService(db, db, new TargetService(db, Clock)).GetDayAsync(userId, Today);
        Assert.Equal(2, day.Entries.Count);
        Assert.Equal(143 + 304, day.Consumed.Kcal, 1);
    }

    [Fact]
    public async Task Create_from_day_snapshots_a_logged_meal()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var egg = SeedFood(db, "Egg", 143, ("1 large egg", 50));
        var oats = SeedFood(db, "Oats", 380);
        await db.SaveChangesAsync(CancellationToken.None);

        var diary = new DiaryService(db, db, new TargetService(db, Clock));
        await diary.AddAsync(userId, new AddDiaryEntryRequest(Today, MealSlot.Breakfast, egg.Id, egg.Portions.First().Id, 2));
        await diary.AddAsync(userId, new AddDiaryEntryRequest(Today, MealSlot.Breakfast, oats.Id, null, 80));

        var created = await Service(db).CreateFromDayAsync(userId, "Snapshot", Today, MealSlot.Breakfast);

        Assert.NotNull(created);
        Assert.Equal(2, created!.Items.Count);
    }

    [Fact]
    public async Task An_all_unknown_template_is_rejected()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);

        var created = await Service(db).CreateAsync(userId, "Bogus", [new MealTemplateItemInput(Guid.NewGuid(), null, 100)]);

        Assert.Null(created); // no real foods ⇒ nothing to save
    }

    [Fact]
    public async Task Templates_are_owner_scoped()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var dbName = $"nf-{Guid.NewGuid()}";

        Guid templateId;
        await using (var aliceDb = Db(alice, dbName))
        {
            var egg = SeedFood(aliceDb, "Egg", 143, ("1 large egg", 50));
            await aliceDb.SaveChangesAsync(CancellationToken.None);
            var created = await Service(aliceDb).CreateAsync(alice, "Alice's meal", [new MealTemplateItemInput(egg.Id, null, 100)]);
            templateId = created!.Id;
        }

        await using var bobDb = Db(bob, dbName);
        var bobSvc = Service(bobDb);

        Assert.Empty(await bobSvc.ListAsync(bob));                                  // not listed
        Assert.Null(await bobSvc.LogAsync(bob, templateId, Today, MealSlot.Lunch)); // can't log
        Assert.False(await bobSvc.DeleteAsync(bob, templateId));                    // can't delete
    }
}
