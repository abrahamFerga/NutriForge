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
        services.AddScoped<MeasurementService>();
        services.AddScoped<HydrationService>();
        services.AddScoped<Consent.ConsentService>();
        services.AddScoped<FavoriteService>();
        services.AddScoped<MealTemplateService>();
        services.AddScoped<DiaryParseService>();
        services.AddScoped<FoodPhotoService>();
        services.AddScoped<Recipes.RecipeService>();
        services.AddScoped<Recipes.RecipeImportService>();
        services.AddScoped<Planning.PantryService>();
        services.AddScoped<Planning.ShoppingListService>();
        services.AddScoped<DietGen.DietPlanGenerator>();
        services.AddScoped<DietGen.DietPlanService>();

        // Notifications: the daily-summary builder + the channel (mock by default; swappable later) +
        // the opt-in subscription / secure account-link service.
        services.AddScoped<Notifications.INotificationChannel, Notifications.MockChannel>();
        services.AddScoped<Notifications.DailySummaryService>();
        services.AddScoped<Notifications.ChannelSubscriptionService>();

        services.AddValidatorsFromAssemblyContaining<SubmitFoodRequestValidator>(ServiceLifetime.Singleton);

        return services;
    }
}
