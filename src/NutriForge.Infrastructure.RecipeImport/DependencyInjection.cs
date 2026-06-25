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

        return services;
    }
}
