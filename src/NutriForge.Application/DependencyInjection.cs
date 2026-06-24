using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NutriForge.Application.Food;
using NutriForge.Application.Tracking;
using NutriForge.Application.Validation;

namespace NutriForge.Application;

/// <summary>Registers the application services and request validators.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<FoodService>();
        services.AddScoped<BarcodeService>();
        services.AddScoped<ProfileService>();
        services.AddScoped<TargetService>();
        services.AddScoped<DiaryService>();
        services.AddScoped<DiaryParseService>();
        services.AddScoped<FoodPhotoService>();
        services.AddScoped<Recipes.RecipeService>();
        services.AddScoped<Recipes.RecipeImportService>();
        services.AddScoped<Planning.PantryService>();
        services.AddScoped<Planning.ShoppingListService>();
        services.AddScoped<DietGen.DietPlanGenerator>();
        services.AddScoped<DietGen.DietPlanService>();

        services.AddValidatorsFromAssemblyContaining<SubmitFoodRequestValidator>(ServiceLifetime.Singleton);

        return services;
    }
}
