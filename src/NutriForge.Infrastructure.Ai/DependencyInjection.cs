using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NutriForge.Infrastructure.Ai.Assistant;

namespace NutriForge.Infrastructure.Ai;

/// <summary>Registers the MAF NutritionAssistant: provider factory, per-request tools, orchestration.</summary>
public static class DependencyInjection
{
    public static IHostApplicationBuilder AddAiAssistant(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = builder.Configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IAssistantAgentFactory, AssistantAgentFactory>();
        builder.Services.AddScoped<AssistantTools>();
        builder.Services.AddScoped<NutritionAssistantService>();

        return builder;
    }
}
