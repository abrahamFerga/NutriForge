using NutriForge.Domain.Common;
using NutriForge.Domain.Diary;

namespace NutriForge.Application.Tracking;

public sealed record AddDiaryEntryRequest(
    DateOnly Date,
    MealSlot MealSlot,
    Guid FoodId,
    Guid? PortionId,
    double Quantity);

public sealed record DiaryEntryDto(
    Guid Id,
    DateOnly Date,
    MealSlot MealSlot,
    int Sequence,
    Guid FoodId,
    string FoodName,
    string PortionName,
    double Quantity,
    double Grams,
    double Kcal,
    double ProteinG,
    double FatG,
    double CarbG);

/// <summary>A day's entries plus the day total vs. the user's target.</summary>
public sealed record DiaryDayDto(
    DateOnly Date,
    IReadOnlyList<DiaryEntryDto> Entries,
    MacroTuple Consumed,
    TargetDto? Target,
    MacroTuple Remaining);

public sealed record MacroTuple(double Kcal, double ProteinG, double FatG, double CarbG)
{
    public static MacroTuple From(Macros m) => new(m.Kcal, m.ProteinG, m.FatG, m.CarbG);
}

/// <summary>One day in the weekly trend: consumed kcal vs. target kcal.</summary>
public sealed record TrendDayDto(DateOnly Date, double Kcal, double TargetKcal);

/// <summary>
/// A one-tap re-loggable food (a recent entry or a favorite) — carries the food + portion + quantity
/// so the SPA can POST it to /diary directly, with the per-entry kcal for display.
/// </summary>
public sealed record QuickAddFoodDto(
    Guid FoodId, string FoodName, Guid? PortionId, string PortionName, double Quantity, double Kcal, double ProteinG);

public sealed record CopyDayRequest(DateOnly? From, DateOnly? To);

internal static class DiaryMappings
{
    public static DiaryEntryDto ToDto(this DiaryEntry e) => new(
        e.Id, e.Date, e.MealSlot, e.Sequence, e.FoodId, e.FoodName, e.PortionName,
        e.Quantity, e.Grams, e.Kcal, e.ProteinG, e.FatG, e.CarbG);
}
