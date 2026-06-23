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

        // The transactional-outbox relay (audit → separate DB).
        services.AddHostedService<OutboxDispatcher>();

        // Diet generation: the OR-Tools LP portion optimizer (REPAIR) + the async generation worker.
        services.AddSingleton<Application.DietGen.IPortionOptimizer, DietGen.OrToolsPortionOptimizer>();
        services.AddHostedService<DietGen.DietPlanGenerationWorker>();

        return builder;
    }
}
