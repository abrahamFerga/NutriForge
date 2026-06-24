using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NutriForge.Application.Abstractions;
using NutriForge.Infrastructure.Caching;
using NutriForge.Infrastructure.Identity;
using NutriForge.Infrastructure.Persistence;
using NutriForge.Infrastructure.Persistence.Interceptors;

namespace NutriForge.Infrastructure;

/// <summary>Wires the persistence, caching, audit-outbox, and clock infrastructure.</summary>
public static class DependencyInjection
{
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        services.AddSingleton<IClock, SystemClock>();
        services.TryAddScoped<ICurrentUser, SystemCurrentUser>(); // hosts (Api) may override
        services.AddScoped<AuditSaveChangesInterceptor>();

        var appDb = builder.Configuration.GetConnectionString("appdb");
        var auditDb = builder.Configuration.GetConnectionString("auditdb");

        services.AddDbContext<NutriForgeDbContext>((sp, options) =>
        {
            options.UseNpgsql(appDb, npgsql => npgsql.EnableRetryOnFailure());
            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });

        services.AddDbContext<AuditDbContext>(options =>
            options.UseNpgsql(auditDb ?? appDb, npgsql => npgsql.EnableRetryOnFailure()));

        services.AddScoped<ICatalogDbContext>(sp => sp.GetRequiredService<NutriForgeDbContext>());
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<NutriForgeDbContext>());

        // Redis (Aspire client integration) + the food-search cache over it.
        builder.AddRedisClient("cache");
        services.AddSingleton<IFoodSearchCache, RedisFoodSearchCache>();

        // The OR-Tools LP portion optimizer (REPAIR) — a dependency of the generator, used by both
        // the synchronous API path and the background generation worker, so it lives here.
        services.AddSingleton<Application.DietGen.IPortionOptimizer, DietGen.OrToolsPortionOptimizer>();

        return builder;
    }

    /// <summary>
    /// The background relays/workers. Register in exactly ONE host so a fan-out doesn't run twice.
    /// Today the API owns them; they are deliberately NOT in <see cref="AddInfrastructure"/> so the
    /// ImportWorker can call AddInfrastructure() for DB access without double-starting them.
    /// </summary>
    public static IHostApplicationBuilder AddInfrastructureBackgroundServices(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The transactional-outbox relay (audit → separate DB).
        builder.Services.AddHostedService<OutboxDispatcher>();
        // Async diet-plan generation.
        builder.Services.AddHostedService<DietGen.DietPlanGenerationWorker>();
        // Scheduled daily calorie summaries to subscribed channels.
        builder.Services.AddHostedService<Notifications.DailySummaryWorker>();

        return builder;
    }
}
