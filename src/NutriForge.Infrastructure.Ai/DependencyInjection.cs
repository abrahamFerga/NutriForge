using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NutriForge.Application.Observability;
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
        builder.Services.AddScoped<LogProposalHolder>();
        builder.Services.AddScoped<AssistantTools>();
        builder.Services.AddScoped<NutritionAssistantService>();
        builder.Services.AddScoped<NutriForge.Application.Abstractions.INlDiaryParser, Assistant.NlDiaryParser>();
        builder.Services.AddScoped<NutriForge.Application.Abstractions.IFoodPhotoParser, Assistant.FoodPhotoParser>();
        builder.Services.AddScoped<NutriForge.Application.Abstractions.IRecipeExtractor, Assistant.RecipeExtractor>();
        // Recipe-generation agent (#101) — writes full recipes from a brief; nutrition resolved by the catalog/reference.
        builder.Services.AddScoped<NutriForge.Application.Abstractions.IRecipeGenAgent, Assistant.RecipeGenAgent>();
        builder.Services.AddScoped<NutriForge.Application.Abstractions.IDietIntentParser, Assistant.DietIntentParser>();
        // SELECT agent (#36) — when present, the diet generator lets it compose the meal selection.
        builder.Services.AddScoped<NutriForge.Application.DietGen.IMealSelectAgent, Assistant.MealSelectAgent>();

        return builder;
    }
}
