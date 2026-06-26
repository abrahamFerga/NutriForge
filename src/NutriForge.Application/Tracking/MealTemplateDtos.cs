using NutriForge.Domain.Common;

namespace NutriForge.Application.Tracking;

/// <summary>One food in a saved meal, with its stored quantity/portion and computed per-item kcal.</summary>
public sealed record MealTemplateItemDto(
    Guid FoodId, string FoodName, Guid? PortionId, string PortionName, double Quantity, double Kcal, double ProteinG);

/// <summary>A saved meal: its items plus the rolled-up totals for display.</summary>
public sealed record MealTemplateDto(
    Guid Id, string Name, IReadOnlyList<MealTemplateItemDto> Items, double Kcal, double ProteinG);

/// <summary>One food to put in a new template.</summary>
public sealed record MealTemplateItemInput(Guid FoodId, Guid? PortionId, double Quantity);

/// <summary>Create a template from an explicit list of foods.</summary>
public sealed record CreateMealTemplateRequest(string Name, IReadOnlyList<MealTemplateItemInput> Items);

/// <summary>Create a template by snapshotting a meal already logged on a day.</summary>
public sealed record SaveMealFromDayRequest(string Name, DateOnly Date, MealSlot MealSlot);

/// <summary>Log a saved template's foods into a day's meal slot.</summary>
public sealed record LogMealTemplateRequest(DateOnly? Date, MealSlot MealSlot);
