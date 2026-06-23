using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Tracking;
using NutriForge.Domain.Catalog;
using NutriForge.Domain.Common;
using NutriForge.Infrastructure.Persistence;

namespace NutriForge.Application.Tests;

public class TrackingServiceTests
{
    private static readonly IClock Clock = new FakeClock(new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Upserting_profile_derives_and_caches_the_target()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        var targets = new TargetService(db, Clock);
        var profiles = new ProfileService(db, targets);

        // The golden worked example (born 1996-06-23 ⇒ age 30 on the clock date).
        await profiles.UpsertAsync(userId, new UpsertProfileRequest(
            Sex.Male, new DateOnly(1996, 6, 23), HeightCm: 180, WeightKg: 80, BodyFatPct: null,
            ActivityLevel.ModeratelyActive, Goal.ModerateCut, MacroStrategy.ProteinAnchored,
            Allergens: null, Dislikes: null, PreferredDiets: null));

        var target = await targets.GetAsync(userId);

        Assert.NotNull(target);
        Assert.Equal(2345, target!.Kcal);
        Assert.Equal(176, target.ProteinG);
    }

    [Fact]
    public async Task Diary_entry_snapshots_nutrition_and_is_immutable_to_later_food_edits()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);

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
        var diary = new DiaryService(db, db, targets);
        var portionId = egg.Portions.First().Id;

        // 2 large eggs = 100 g → one whole per-100g profile.
        var entry = await diary.AddAsync(userId,
            new AddDiaryEntryRequest(Clock.Today, MealSlot.Breakfast, egg.Id, portionId, Quantity: 2));

        Assert.Equal(100, entry.Grams);
        Assert.Equal(143, entry.Kcal);
        Assert.Equal(13, entry.ProteinG); // 12.6 rounded

        // Mutate the catalog food; the logged snapshot must not change (ADR-0006).
        egg.NutrientProfile.KcalPer100g = 999;
        await db.SaveChangesAsync(CancellationToken.None);

        var day = await diary.GetDayAsync(userId, Clock.Today);
        Assert.Equal(143, day.Consumed.Kcal);
        Assert.Single(day.Entries);
    }

    [Fact]
    public async Task Query_filter_isolates_one_users_diary_from_another()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        // Shared database name so both contexts see the same store, different principals.
        var dbName = $"iso-{Guid.NewGuid()}";
        await using var aliceDb = TestDbNamed(alice, dbName);
        var egg = new Domain.Catalog.Food
        {
            Name = "Egg",
            Source = new FoodSource("seed", "egg"),
            NutrientProfile = new NutrientProfile { KcalPer100g = 143, ProteinPer100g = 12.6, FatPer100g = 9.5, CarbPer100g = 0.7 },
        };
        egg.AddPortion("1 egg", 50);
        aliceDb.Foods.Add(egg);
        await aliceDb.SaveChangesAsync(CancellationToken.None);

        var aliceDiary = new DiaryService(aliceDb, aliceDb, new TargetService(aliceDb, Clock));
        await aliceDiary.AddAsync(alice, new AddDiaryEntryRequest(Clock.Today, MealSlot.Lunch, egg.Id, egg.Portions.First().Id, 1));

        await using var bobDb = TestDbNamed(bob, dbName);
        var bobDay = await new DiaryService(bobDb, bobDb, new TargetService(bobDb, Clock)).GetDayAsync(bob, Clock.Today);

        Assert.Empty(bobDay.Entries); // Bob can never see Alice's rows.
    }

    private static NutriForgeDbContext TestDbNamed(Guid userId, string dbName)
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<NutriForgeDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new NutriForgeDbContext(options, new FakeCurrentUser(userId));
    }
}
