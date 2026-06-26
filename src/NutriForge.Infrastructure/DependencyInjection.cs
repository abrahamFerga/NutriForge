using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Notifications;
using NutriForge.Infrastructure.Caching;
using NutriForge.Infrastructure.Identity;
using NutriForge.Infrastructure.Notifications;
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
        services.AddSingleton<IWhatsAppPendingStore, Notifications.RedisWhatsAppPendingStore>();

        // The OR-Tools LP portion optimizer (REPAIR) — a dependency of the generator, used by both
        // the synchronous API path and the background generation worker, so it lives here.
        services.AddSingleton<Application.DietGen.IPortionOptimizer, DietGen.OrToolsPortionOptimizer>();

        // USDA FoodData Central importer (#19) — idempotent catalog upsert; driven by the import worker.
        services.AddScoped<UsdaFdc.UsdaFdcImporter>();

        // Twilio WhatsApp channel (#96) — replaces the MockChannel when credentials are configured.
        AddNotificationChannel(services, builder.Configuration);

        // Inbound WhatsApp webhook signature validation (#97 hardening). The endpoint is anonymous, so
        // the X-Twilio-Signature is its only authentication. Registered UNCONDITIONALLY (independent of
        // whether the outbound channel is enabled): the validator fails closed when no auth token is set,
        // so a misconfigured/disabled channel rejects inbound traffic rather than trusting a spoofable sender.
        var twilioOptions = builder.Configuration.GetSection(TwilioOptions.SectionName).Get<TwilioOptions>() ?? new TwilioOptions();
        services.AddSingleton<Application.Notifications.IWhatsAppRequestValidator>(
            new Notifications.TwilioRequestValidator(twilioOptions.AuthToken));

        // Connector registry (#14): the config-driven list of outbound connectors + a persisted
        // last-run store, surfaced read-only behind the admin endpoint. Descriptors are computed
        // from configuration once at startup so adding a connector is a one-line descriptor.
        AddConnectorRegistry(services, builder.Configuration);

        return builder;
    }

    private static void AddConnectorRegistry(IServiceCollection services, IConfiguration config)
    {
        foreach (var descriptor in BuildConnectorDescriptors(config))
        {
            services.AddSingleton(descriptor);
        }

        services.AddScoped<Application.Connectors.IConnectorRunStore, Connectors.ConnectorRunStore>();
        services.AddScoped<Application.Connectors.IConnectorRegistry, Connectors.ConnectorRegistry>();
    }

    private static IEnumerable<Application.Connectors.ConnectorDescriptor> BuildConnectorDescriptors(IConfiguration config)
    {
        const string outbound = "outbound";
        var twilioOpts = config.GetSection(TwilioOptions.SectionName).Get<TwilioOptions>() ?? new TwilioOptions();
        return
        [
            // The nightly USDA importer — "configured" once it has a CSV directory to read.
            new("usda-fdc", "USDA FoodData Central", Application.Connectors.ConnectorKind.FoodSource, outbound,
                Configured: !string.IsNullOrWhiteSpace(config["Usda:DataDirectory"])),
            // Open Food Facts barcode lookups — the typed client is always registered and needs no key.
            new("open-food-facts", "Open Food Facts", Application.Connectors.ConnectorKind.FoodSource, outbound,
                Configured: true),
            // The chat/vision LLM provider — configured when a provider (and key, where required) is set.
            new("llm", "LLM provider", Application.Connectors.ConnectorKind.LlmProvider, outbound,
                Configured: IsLlmConfigured(config)),
            // Twilio WhatsApp notification channel (#96).
            new("twilio-whatsapp", "Twilio WhatsApp", Application.Connectors.ConnectorKind.Notification, outbound,
                Configured: twilioOpts.IsConfigured),
        ];
    }

    private static void AddNotificationChannel(IServiceCollection services, IConfiguration config)
    {
        var provider = (config["Channels:WhatsApp:Provider"] ?? "mock").Trim().ToLowerInvariant();
        if (provider != "twilio")
        {
            return;
        }

        var opts = config.GetSection(TwilioOptions.SectionName).Get<TwilioOptions>() ?? new TwilioOptions();
        if (!opts.IsConfigured)
        {
            return;
        }

        services.AddSingleton(opts);
        services.AddHttpClient<TwilioWhatsAppChannel>();
        services.Replace(ServiceDescriptor.Scoped<INotificationChannel, TwilioWhatsAppChannel>());
    }

    /// <summary>Mirrors the assistant factory: a provider is selected, and a key is present where one is required.</summary>
    private static bool IsLlmConfigured(IConfiguration config)
    {
        var provider = (config["Ai:Provider"] ?? "None").Trim().ToLowerInvariant();
        if (provider is "" or "none")
        {
            return false;
        }

        // OpenAI/Azure need an API key; local providers (e.g. ollama) don't.
        return provider is "openai" or "azureopenai" or "azure"
            ? !string.IsNullOrWhiteSpace(config["Ai:ApiKey"])
            : true;
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
        // Scheduled notification workers: daily summaries, weekly digests, log reminders.
        builder.Services.AddHostedService<Notifications.DailySummaryWorker>();
        builder.Services.AddHostedService<Notifications.WeeklySummaryWorker>();
        builder.Services.AddHostedService<Notifications.LogReminderWorker>();

        return builder;
    }
}
