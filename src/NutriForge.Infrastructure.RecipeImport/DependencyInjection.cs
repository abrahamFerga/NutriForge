using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using NutriForge.Application.Abstractions;

namespace NutriForge.Infrastructure.RecipeImport;

/// <summary>Registers the recipe-import connectors (resilience comes from ServiceDefaults).</summary>
public static class DependencyInjection
{
    /// <summary>Registers the YouTube metadata client; <paramref name="youTubeApiKey"/> (optional) enables description fetch.</summary>
    public static IServiceCollection AddRecipeImport(this IServiceCollection services, string? youTubeApiKey = null)
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

        return services;
    }
}
