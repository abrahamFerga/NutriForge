using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Recipes;

namespace NutriForge.Infrastructure.RecipeImport;

/// <summary>Registers the recipe-import connectors (resilience comes from ServiceDefaults).</summary>
public static class DependencyInjection
{
    /// <summary>Registers the YouTube metadata client; <paramref name="youTubeApiKey"/> (optional) enables description fetch.</summary>
    public static IServiceCollection AddRecipeImport(
        this IServiceCollection services, string? youTubeApiKey = null, IConfiguration? config = null)
    {
        services.AddSingleton(new YouTubeOptions { ApiKey = youTubeApiKey });

        services.AddHttpClient<IYouTubeMetadataClient, YouTubeMetadataClient>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NutriForge/1.0 (+https://github.com/abrahamFerga/NutriForge)");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        // Web recipe fetcher (#91): a DEDICATED client with an SSRF-guarded connect callback — it must
        // not share the generic resilience client, and every connection (incl. redirects) is vetted.
        services.AddHttpClient<IRecipeUrlFetcher, RecipeUrlFetcher>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NutriForge/1.0 (+https://github.com/abrahamFerga/NutriForge)");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.MaxResponseContentBufferSize = 3_000_000;
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectCallback = RecipeUrlFetcher.ConnectGuardedAsync,
        });

        // Opt-in transcript enrichment (#93, ADR-0015). Default OFF; enabled via
        // TranscriptEnrichment:Provider = "youtube-explode" | "vendor".
        AddTranscriptSource(services, config);

        // TranscriptEnrichmentJob is scoped (takes ICatalogDbContext).
        services.AddScoped<TranscriptEnrichmentJob>();

        return services;
    }

    private static void AddTranscriptSource(IServiceCollection services, IConfiguration? config)
    {
        var opts = config?.GetSection(TranscriptEnrichmentOptions.SectionName)
                       .Get<TranscriptEnrichmentOptions>()
                   ?? new TranscriptEnrichmentOptions();
        services.AddSingleton(opts);

        if (opts.Provider.Equals("youtube-explode", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<ITranscriptSource, YoutubeExplodeTranscriptSource>();
        }
        else if (opts.Provider.Equals("vendor", StringComparison.OrdinalIgnoreCase)
                 && !string.IsNullOrWhiteSpace(opts.VendorUrl))
        {
            services.AddHttpClient<ManagedVendorTranscriptSource>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });
            services.AddSingleton<ITranscriptSource, ManagedVendorTranscriptSource>();
        }
        else
        {
            services.AddSingleton<ITranscriptSource, NullTranscriptSource>();
        }
    }
}
