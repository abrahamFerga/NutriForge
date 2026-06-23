using Microsoft.Extensions.DependencyInjection;
using NutriForge.Application.Abstractions;

namespace NutriForge.Infrastructure.OpenFoodFacts;

/// <summary>Registers the Open Food Facts typed client (resilience comes from ServiceDefaults).</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddOpenFoodFacts(this IServiceCollection services)
    {
        services.AddHttpClient<IOpenFoodFactsClient, OpenFoodFactsClient>(client =>
        {
            client.BaseAddress = new Uri("https://world.openfoodfacts.org");
            // OFF requires a descriptive User-Agent identifying the app.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NutriForge/1.0 (+https://github.com/abrahamFerga/NutriForge)");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        return services;
    }
}
