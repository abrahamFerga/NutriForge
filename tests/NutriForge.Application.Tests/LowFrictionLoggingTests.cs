using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Food;
using NutriForge.Application.Tracking;
using NutriForge.Domain.Catalog;
using NutriForge.Domain.Common;
using NutriForge.Infrastructure.OpenFoodFacts;

namespace NutriForge.Application.Tests;

/// <summary>Epic 4 — natural-language parsing and barcode (Open Food Facts) logging.</summary>
public sealed class LowFrictionLoggingTests
{
    private static readonly IClock Clock = new FakeClock(new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero));

    private sealed class NoOpCache : IFoodSearchCache
    {
        public Task<IReadOnlyList<FoodSummary>?> GetAsync(string q, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FoodSummary>?>(null);
        public Task SetAsync(string q, IReadOnlyList<FoodSummary> r, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeParser(params ParsedFoodItem[] items) : INlDiaryParser
    {
        public bool IsConfigured => true;
        public Task<IReadOnlyList<ParsedFoodItem>> ParseAsync(string text, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ParsedFoodItem>>(items);
    }

    private sealed class FakeOff(Domain.Catalog.Food? result) : IOpenFoodFactsClient
    {
        public Domain.Catalog.Food? Result { get; set; } = result;
        public int Calls { get; private set; }
        public Task<Domain.Catalog.Food?> FetchByBarcodeAsync(string gtin, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
    }

    private static async Task SeedEggAsync(NutriForge.Infrastructure.Persistence.NutriForgeDbContext db)
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
    }

    [Fact]
    public async Task Nl_parse_resolves_known_foods_to_candidates_and_flags_the_rest()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);
        await SeedEggAsync(db);

        var targets = new TargetService(db, Clock);
        var parse = new DiaryParseService(
            new FakeParser(new ParsedFoodItem("egg", 2, null), new ParsedFoodItem("unicorn meat", 1, null)),
            new FoodService(db, new NoOpCache()),
            new DiaryService(db, db, targets),
            Clock);

        var result = await parse.ParseAsync(userId, "2 eggs and unicorn meat", MealSlot.Breakfast, null, CancellationToken.None);

        // "egg" resolves to a candidate with the code-owned snapshot (2 × 50 g = 100 g = 143 kcal)...
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(143, candidate.Kcal);
        Assert.Equal("Breakfast", candidate.MealSlot);
        // ...and the made-up food is flagged, not invented.
        Assert.Contains("unicorn meat", result.Unresolved);
    }

    [Fact]
    public async Task Barcode_lookup_fetches_from_off_on_miss_then_serves_from_catalog()
    {
        var userId = Guid.NewGuid();
        await using var db = TestDb.New(userId);

        var scanned = new Domain.Catalog.Food
        {
            Name = "Scanned bar",
            Gtin = "999",
            VerificationStatus = VerificationStatus.Community,
            Source = new FoodSource("openfoodfacts", "999"),
            NutrientProfile = new NutrientProfile { KcalPer100g = 200, ProteinPer100g = 10, FatPer100g = 8, CarbPer100g = 22 },
        };
        scanned.AddPortion("100 g", 100);

        var off = new FakeOff(scanned);
        var svc = new BarcodeService(db, off);

        var first = await svc.LookupAsync("999", CancellationToken.None);
        Assert.NotNull(first);
        Assert.Equal(1, off.Calls);
        Assert.Equal(1, await db.Foods.CountAsync(CancellationToken.None)); // upserted into the catalog

        off.Result = null; // OFF would now miss — but the catalog has it
        var second = await svc.LookupAsync("999", CancellationToken.None);
        Assert.NotNull(second);
        Assert.Equal(1, off.Calls); // not called again — served from the catalog
    }

    [Fact]
    public async Task Off_client_maps_a_product_to_a_per_100g_food()
    {
        const string json = """
        {"status":1,"product":{"code":"5012345678900","product_name":"Test Protein Bar","brands":"Acme, Other",
        "serving_size":"60 g","nutriments":{"energy-kcal_100g":350,"proteins_100g":30,"fat_100g":12,"carbohydrates_100g":35,"sodium_100g":0.4}}}
        """;
        using var http = new HttpClient(new StubHandler(json)) { BaseAddress = new Uri("https://world.openfoodfacts.org") };
        var client = new OpenFoodFactsClient(http, NullLogger<OpenFoodFactsClient>.Instance);

        var food = await client.FetchByBarcodeAsync("5012345678900", CancellationToken.None);

        Assert.NotNull(food);
        Assert.Equal("Test Protein Bar", food!.Name);
        Assert.Equal("Acme", food.Brand); // first brand only
        Assert.Equal("5012345678900", food.Gtin);
        Assert.Equal(VerificationStatus.Community, food.VerificationStatus);
        Assert.Equal(350, food.NutrientProfile.KcalPer100g);
        Assert.Equal(400, food.NutrientProfile.SodiumMgPer100g); // 0.4 g → 400 mg
        Assert.Contains(food.Portions, p => p.Grams == 60); // serving parsed
    }
}
