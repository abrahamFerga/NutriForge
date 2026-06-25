using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Domain.Assistant;
using NutriForge.Domain.Catalog;
using NutriForge.Domain.Common;
using NutriForge.Domain.Connectors;
using NutriForge.Domain.Diary;
using NutriForge.Domain.Planning;
using NutriForge.Domain.Recipes;
using NutriForge.Domain.Users;

namespace NutriForge.Infrastructure.Persistence;

/// <summary>
/// The operational store (database <c>appdb</c>). Holds the public-read catalog (schema
/// <c>catalog</c>) and the user-owned data + infra tables (schema <c>app</c>). A global query
/// filter isolates every <see cref="IUserOwned"/> entity by the current user (ADR-0001); the
/// catalog foods/ingredients stay unfiltered, but <see cref="Recipe"/> is owner-scoped (a recipe is
/// visible when it is global — no owner — or owned by the current user). Implements the Application
/// context ports so handlers stay provider-free.
/// </summary>
/// <remarks>
/// v1 uses a single operational context for delivery simplicity; the schema split keeps the
/// catalog/user boundary honest and leaves room to split into per-context DbContexts later
/// (ARCH "schema-per-bounded-context"). The audit log lives in a separate context/database.
/// </remarks>
public sealed class NutriForgeDbContext(DbContextOptions<NutriForgeDbContext> options, ICurrentUser currentUser)
    : DbContext(options), ICatalogDbContext, IAppDbContext
{
    /// <summary>Referenced by the global query filter; EF parameterizes it per-query.</summary>
    public Guid CurrentUserId => currentUser.UserId ?? Guid.Empty;

    // Catalog (public-read)
    public DbSet<Domain.Catalog.Food> Foods => Set<Domain.Catalog.Food>();
    public DbSet<Portion> Portions => Set<Portion>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<DietType> DietTypes => Set<DietType>();

    // User-owned
    public DbSet<User> Users => Set<User>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<Target> Targets => Set<Target>();
    public DbSet<DiaryEntry> DiaryEntries => Set<DiaryEntry>();
    public DbSet<BodyMeasurement> BodyMeasurements => Set<BodyMeasurement>();
    public DbSet<FavoriteFood> FavoriteFoods => Set<FavoriteFood>();
    public DbSet<MealTemplate> MealTemplates => Set<MealTemplate>();
    public DbSet<AssistantSession> AssistantSessions => Set<AssistantSession>();
    public DbSet<PantryItem> PantryItems => Set<PantryItem>();
    public DbSet<ShoppingList> ShoppingLists => Set<ShoppingList>();
    public DbSet<MealPlan> MealPlans => Set<MealPlan>();
    public DbSet<Domain.Notifications.ChannelMessage> ChannelMessages => Set<Domain.Notifications.ChannelMessage>();
    public DbSet<Domain.Notifications.ChannelSubscription> ChannelSubscriptions => Set<Domain.Notifications.ChannelSubscription>();
    public DbSet<Domain.Notifications.AccountLinkToken> AccountLinkTokens => Set<Domain.Notifications.AccountLinkToken>();

    // Operational infra
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<ConnectorRun> ConnectorRuns => Set<ConnectorRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        var b = modelBuilder;

        // ---- Catalog schema ----
        b.Entity<Domain.Catalog.Food>(e =>
        {
            e.ToTable("foods", "catalog");
            e.HasKey(f => f.Id);
            e.Property(f => f.Name).HasMaxLength(200).IsRequired();
            e.Property(f => f.Brand).HasMaxLength(200);
            e.Property(f => f.Gtin).HasMaxLength(14);
            e.HasIndex(f => f.Gtin);
            e.Property(f => f.VerificationStatus).HasConversion<string>().HasMaxLength(20);

            e.OwnsOne(f => f.Source, s =>
            {
                s.Property(p => p.Provider).HasColumnName("source_provider").HasMaxLength(50).IsRequired();
                s.Property(p => p.ProviderId).HasColumnName("source_provider_id").HasMaxLength(100).IsRequired();
                s.HasIndex(p => new { p.Provider, p.ProviderId }).IsUnique();
            });
            e.Navigation(f => f.Source).IsRequired();

            e.OwnsOne(f => f.NutrientProfile, n =>
            {
                n.Property(p => p.KcalPer100g).HasColumnName("kcal_per_100g");
                n.Property(p => p.ProteinPer100g).HasColumnName("protein_per_100g");
                n.Property(p => p.FatPer100g).HasColumnName("fat_per_100g");
                n.Property(p => p.CarbPer100g).HasColumnName("carb_per_100g");
                n.Property(p => p.FiberPer100g).HasColumnName("fiber_per_100g");
                n.Property(p => p.SugarPer100g).HasColumnName("sugar_per_100g");
                n.Property(p => p.SodiumMgPer100g).HasColumnName("sodium_mg_per_100g");
            });
            e.Navigation(f => f.NutrientProfile).IsRequired();

            e.HasMany(f => f.Portions).WithOne().HasForeignKey(p => p.FoodId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(f => f.Portions).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        b.Entity<Portion>(e =>
        {
            e.ToTable("portions", "catalog");
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).HasMaxLength(100).IsRequired();
        });

        b.Entity<Ingredient>(e =>
        {
            e.ToTable("ingredients", "catalog");
            e.HasKey(i => i.Id);
            e.Property(i => i.CanonicalName).HasMaxLength(200).IsRequired();
            e.Property(i => i.AisleCategory).HasMaxLength(50);
            e.HasIndex(i => i.CanonicalName);
            // Raw↔cooked yield (#85): defaults backfill existing rows as "eaten as-is, stated raw".
            e.Property(i => i.YieldFactor).HasDefaultValue(1.0);
            e.Property(i => i.RecipeGramsAreRaw).HasDefaultValue(true);
        });

        b.Entity<Recipe>(e =>
        {
            e.ToTable("recipes", "catalog");
            e.HasKey(r => r.Id);
            e.Property(r => r.Name).HasMaxLength(300).IsRequired();
            e.Property(r => r.CookMethod).HasMaxLength(40);
            e.Property(r => r.SourceUrl).HasMaxLength(2048);
            e.Property(r => r.SourceType).HasMaxLength(20);
            e.Property(r => r.SourceVideoId).HasMaxLength(20);
            e.Property(r => r.SourceKey).HasMaxLength(400);
            e.Property(r => r.ThumbnailUrl).HasMaxLength(2048);
            // Web-import dedup: one recipe per owner per normalized source page (YouTube uses the video index).
            e.HasIndex(r => new { r.OwnerUserId, r.SourceKey }).IsUnique().HasFilter("\"SourceKey\" IS NOT NULL");
            e.HasMany(r => r.Ingredients).WithOne().HasForeignKey(i => i.RecipeId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(r => r.Ingredients).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasIndex(r => r.IsNutritionComputed);
            // Owner-scoped: null OwnerUserId = a GLOBAL (admin-curated) recipe; otherwise private to a user.
            e.HasIndex(r => r.OwnerUserId);
            // Dedup imported videos PER OWNER: one global copy AND one private copy per user are both
            // allowed (each user can import the same video), but a given owner can't hold two of the same.
            // Partial index ignores hand-authored recipes (null video id).
            e.HasIndex(r => new { r.OwnerUserId, r.SourceVideoId }).IsUnique().HasFilter("\"SourceVideoId\" IS NOT NULL");
            // Visibility: everyone reads global recipes (OwnerUserId IS NULL) plus their own. The catalog
            // food/ingredient tables stay unfiltered; only recipes carry an owner.
            e.HasQueryFilter(r => r.OwnerUserId == null || r.OwnerUserId == CurrentUserId);
        });

        b.Entity<RecipeIngredient>(e =>
        {
            e.ToTable("recipe_ingredients", "catalog");
            e.HasKey(i => i.Id);
            e.Property(i => i.RawText).HasMaxLength(300);
            e.Property(i => i.IngredientName).HasMaxLength(200);
            e.Property(i => i.Unit).HasMaxLength(40);
        });

        b.Entity<DietType>(e =>
        {
            e.ToTable("diet_types", "catalog");
            e.HasKey(d => d.Id);
            e.Property(d => d.Slug).HasMaxLength(50).IsRequired();
            e.Property(d => d.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(d => d.Slug).IsUnique();
        });

        // ---- App schema (user-owned) ----
        b.Entity<User>(e =>
        {
            e.ToTable("users", "app");
            e.HasKey(u => u.Id);
            e.Property(u => u.OidcSubject).HasMaxLength(200).IsRequired();
            e.HasIndex(u => u.OidcSubject).IsUnique();
            e.Property(u => u.Email).HasMaxLength(320);
            e.Property(u => u.DisplayName).HasMaxLength(200);
        });

        b.Entity<Profile>(e =>
        {
            e.ToTable("profiles", "app");
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.UserId).IsUnique();
            e.Property(p => p.Sex).HasConversion<string>().HasMaxLength(10);
            e.Property(p => p.Activity).HasConversion<string>().HasMaxLength(20);
            e.Property(p => p.Goal).HasConversion<string>().HasMaxLength(20);
            e.Property(p => p.MacroStrategy).HasConversion<string>().HasMaxLength(20);
            e.HasQueryFilter(p => p.UserId == CurrentUserId);
        });

        b.Entity<Target>(e =>
        {
            e.ToTable("targets", "app");
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.UserId).IsUnique();
            e.Property(t => t.Formula).HasMaxLength(300);
            e.HasQueryFilter(t => t.UserId == CurrentUserId);
        });

        b.Entity<DiaryEntry>(e =>
        {
            e.ToTable("diary_entries", "app");
            e.HasKey(d => d.Id);
            e.HasIndex(d => new { d.UserId, d.Date });
            e.Property(d => d.MealSlot).HasConversion<string>().HasMaxLength(20);
            e.Property(d => d.FoodName).HasMaxLength(200);
            e.Property(d => d.PortionName).HasMaxLength(100);
            e.HasQueryFilter(d => d.UserId == CurrentUserId);
        });

        b.Entity<BodyMeasurement>(e =>
        {
            e.ToTable("body_measurements", "app");
            e.HasKey(m => m.Id);
            e.HasIndex(m => new { m.UserId, m.Date }).IsUnique(); // one entry per day per user
            e.HasQueryFilter(m => m.UserId == CurrentUserId);
        });

        b.Entity<FavoriteFood>(e =>
        {
            e.ToTable("favorite_foods", "app");
            e.HasKey(f => f.Id);
            e.HasIndex(f => new { f.UserId, f.FoodId }).IsUnique(); // at most one per (user, food)
            e.HasQueryFilter(f => f.UserId == CurrentUserId);
        });

        // Saved meals (#70): an owner-scoped aggregate with owned item children.
        b.Entity<MealTemplate>(e =>
        {
            e.ToTable("meal_templates", "app");
            e.HasKey(t => t.Id);
            e.Property(t => t.Name).HasMaxLength(120).IsRequired();
            e.HasIndex(t => t.UserId);
            e.HasQueryFilter(t => t.UserId == CurrentUserId);
            e.HasMany(t => t.Items).WithOne().HasForeignKey(i => i.MealTemplateId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(t => t.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        b.Entity<MealTemplateItem>(e =>
        {
            e.ToTable("meal_template_items", "app");
            e.HasKey(i => i.Id);
        });

        b.Entity<AssistantSession>(e =>
        {
            e.ToTable("assistant_sessions", "app");
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.UserId).IsUnique();
            e.Property(s => s.Data).HasColumnType("jsonb");
            e.HasQueryFilter(s => s.UserId == CurrentUserId);
        });

        b.Entity<PantryItem>(e =>
        {
            e.ToTable("pantry_items", "app");
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.UserId);
            e.Property(p => p.IngredientName).HasMaxLength(200);
            e.HasQueryFilter(p => p.UserId == CurrentUserId);
        });

        b.Entity<ShoppingList>(e =>
        {
            e.ToTable("shopping_lists", "app");
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.UserId);
            e.HasMany(s => s.Items).WithOne().HasForeignKey(i => i.ShoppingListId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(s => s.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasQueryFilter(s => s.UserId == CurrentUserId);
        });

        b.Entity<ShoppingItem>(e =>
        {
            e.ToTable("shopping_items", "app");
            e.HasKey(i => i.Id);
            e.Property(i => i.IngredientName).HasMaxLength(200);
            e.Property(i => i.AisleCategory).HasMaxLength(50);
        });

        b.Entity<MealPlan>(e =>
        {
            e.ToTable("meal_plans", "app");
            e.HasKey(m => m.Id);
            e.HasIndex(m => new { m.UserId, m.Status });
            e.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(m => m.IntentJson).HasColumnType("jsonb");
            // Server default 1 backfills existing single-eater plans (additive, no behaviour change).
            e.Property(m => m.Eaters).HasDefaultValue(1);
            // Day-block rotation; default 1 backfills existing plans as "a fresh meal-set every day".
            e.Property(m => m.BlockSize).HasDefaultValue(1);
            e.HasMany(m => m.Slots).WithOne().HasForeignKey(s => s.MealPlanId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(m => m.Slots).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasMany(m => m.Members).WithOne().HasForeignKey(x => x.MealPlanId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(m => m.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasQueryFilter(m => m.UserId == CurrentUserId);
        });

        b.Entity<PlanSlot>(e =>
        {
            e.ToTable("plan_slots", "app");
            e.HasKey(s => s.Id);
            e.Property(s => s.MealSlot).HasConversion<string>().HasMaxLength(20);
            e.Property(s => s.RecipeName).HasMaxLength(300);
        });

        // Owned via the filtered MealPlan navigation (no own query filter, no direct endpoint).
        b.Entity<PlanMember>(e =>
        {
            e.ToTable("plan_members", "app");
            e.HasKey(m => m.Id);
            e.Property(m => m.Name).HasMaxLength(120).IsRequired();
        });

        b.Entity<Domain.Notifications.ChannelMessage>(e =>
        {
            e.ToTable("channel_messages", "app");
            e.HasKey(m => m.Id);
            e.Property(m => m.ChannelName).HasMaxLength(20);
            e.Property(m => m.ChatId).HasMaxLength(64);
            e.Property(m => m.Body).HasMaxLength(2000);
            e.HasIndex(m => new { m.UserId, m.CreatedAt });
            e.HasQueryFilter(m => m.UserId == CurrentUserId);
        });

        b.Entity<Domain.Notifications.ChannelSubscription>(e =>
        {
            e.ToTable("channel_subscriptions", "app");
            e.HasKey(s => s.Id);
            e.Property(s => s.Channel).HasMaxLength(20).IsRequired();
            e.Property(s => s.Address).HasMaxLength(128);
            // One subscription per channel per user.
            e.HasIndex(s => new { s.UserId, s.Channel }).IsUnique();
            e.HasQueryFilter(s => s.UserId == CurrentUserId);
        });

        b.Entity<Domain.Notifications.AccountLinkToken>(e =>
        {
            e.ToTable("account_link_tokens", "app");
            e.HasKey(t => t.Id);
            e.Property(t => t.Channel).HasMaxLength(20).IsRequired();
            e.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            // Redeemed by hash lookup; unique so a hash collision can't shadow another user's link.
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasQueryFilter(t => t.UserId == CurrentUserId);
        });

        // ---- Operational infra ----
        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox", "app");
            e.HasKey(m => m.Id);
            e.Property(m => m.Type).HasMaxLength(50).IsRequired();
            e.HasIndex(m => m.ProcessedAt);
        });

        b.Entity<IdempotencyRecord>(e =>
        {
            e.ToTable("idempotency", "app");
            e.HasKey(r => r.Id);
            e.Property(r => r.Key).HasMaxLength(200).IsRequired();
            e.HasIndex(r => new { r.Key, r.UserId }).IsUnique();
        });

        // Connector registry last-run log (#14): one upserted row per connector, keyed by its stable id.
        b.Entity<ConnectorRun>(e =>
        {
            e.ToTable("connector_runs", "ops");
            e.HasKey(r => r.ConnectorKey);
            e.Property(r => r.ConnectorKey).HasMaxLength(50);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(r => r.Detail).HasMaxLength(1000);
        });
    }
}
