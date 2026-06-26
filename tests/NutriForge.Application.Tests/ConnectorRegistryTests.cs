using NutriForge.Application.Connectors;
using NutriForge.Infrastructure.Connectors;
using NutriForge.Infrastructure.Persistence;

namespace NutriForge.Application.Tests;

/// <summary>#14 — the config-driven connector registry + its persisted last-run store.</summary>
public sealed class ConnectorRegistryTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 25, 8, 0, 0, TimeSpan.Zero);

    private static (ConnectorRunStore Store, ConnectorRegistry Registry) Build(
        NutriForgeDbContext db, params ConnectorDescriptor[] descriptors)
    {
        var store = new ConnectorRunStore(db, new FakeClock(Now));
        return (store, new ConnectorRegistry(descriptors, store));
    }

    [Fact]
    public async Task Registry_lists_every_descriptor_and_marks_never_run_connectors()
    {
        await using var db = TestDb.New(Guid.NewGuid());
        var (_, registry) = Build(
            db,
            new ConnectorDescriptor("usda-fdc", "USDA FoodData Central", ConnectorKind.FoodSource, "outbound", Configured: false),
            new ConnectorDescriptor("open-food-facts", "Open Food Facts", ConnectorKind.FoodSource, "outbound", Configured: true));

        var status = await registry.GetStatusAsync();

        Assert.Equal(2, status.Count);
        var usda = status.Single(c => c.Key == "usda-fdc");
        Assert.False(usda.Configured);
        Assert.Null(usda.LastRun); // never run
        Assert.Equal("FoodSource", usda.Kind);
        Assert.True(status.Single(c => c.Key == "open-food-facts").Configured);
    }

    [Fact]
    public async Task Successful_run_is_recorded_and_surfaced_by_the_registry()
    {
        await using var db = TestDb.New(Guid.NewGuid());
        var (store, registry) = Build(
            db, new ConnectorDescriptor("usda-fdc", "USDA FoodData Central", ConnectorKind.FoodSource, "outbound", Configured: true));

        await store.BeginAsync("usda-fdc");
        await store.CompleteAsync("usda-fdc", itemsProcessed: 42, detail: "42 inserted");

        var usda = Assert.Single(await registry.GetStatusAsync());
        Assert.NotNull(usda.LastRun);
        Assert.Equal("Success", usda.LastRun!.Status);
        Assert.Equal(42, usda.LastRun.ItemsProcessed);
        Assert.Equal("42 inserted", usda.LastRun.Detail);
        Assert.NotNull(usda.LastRun.CompletedAt);
    }

    [Fact]
    public async Task A_later_run_upserts_the_same_key_rather_than_adding_a_row()
    {
        await using var db = TestDb.New(Guid.NewGuid());
        var (store, registry) = Build(
            db, new ConnectorDescriptor("usda-fdc", "USDA FoodData Central", ConnectorKind.FoodSource, "outbound", Configured: true));

        await store.BeginAsync("usda-fdc");
        await store.CompleteAsync("usda-fdc", 10);
        // A later run fails — same key, so it overwrites rather than appending.
        await store.BeginAsync("usda-fdc");
        await store.FailAsync("usda-fdc", "boom");

        Assert.Single(await store.LatestByKeyAsync()); // one row per key
        var usda = Assert.Single(await registry.GetStatusAsync());
        Assert.Equal("Failed", usda.LastRun!.Status);
        Assert.Equal("boom", usda.LastRun.Detail);
    }
}
