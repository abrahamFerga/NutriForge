using NutriForge.Domain.Catalog;

namespace NutriForge.Application.Food;

/// <summary>A search-result row — enough to rank, show, and pick a portion to log.</summary>
public sealed record FoodSummary(
    Guid Id,
    string Name,
    string? Brand,
    string VerificationStatus,
    double KcalPer100g,
    double ProteinPer100g,
    double FatPer100g,
    double CarbPer100g,
    IReadOnlyList<PortionDto> Portions);

public sealed record PortionDto(Guid Id, string Name, double Grams);

/// <summary>Full food detail including micros.</summary>
public sealed record FoodDetail(
    Guid Id,
    string Name,
    string? Brand,
    string? Gtin,
    string VerificationStatus,
    string SourceProvider,
    double KcalPer100g,
    double ProteinPer100g,
    double FatPer100g,
    double CarbPer100g,
    double? FiberPer100g,
    double? SugarPer100g,
    double? SodiumMgPer100g,
    IReadOnlyList<PortionDto> Portions);

/// <summary>A user-submitted food (lands at the lowest verification tier).</summary>
public sealed record SubmitFoodRequest(
    string Name,
    string? Brand,
    string? Gtin,
    double KcalPer100g,
    double ProteinPer100g,
    double FatPer100g,
    double CarbPer100g,
    IReadOnlyList<SubmitPortion>? Portions);

public sealed record SubmitPortion(string Name, double Grams);

internal static class FoodMappings
{
    public static FoodSummary ToSummary(this Domain.Catalog.Food f) => new(
        f.Id, f.Name, f.Brand, f.VerificationStatus.ToString(),
        f.NutrientProfile.KcalPer100g, f.NutrientProfile.ProteinPer100g,
        f.NutrientProfile.FatPer100g, f.NutrientProfile.CarbPer100g,
        f.Portions.Select(p => new PortionDto(p.Id, p.Name, p.Grams)).ToList());

    public static FoodDetail ToDetail(this Domain.Catalog.Food f) => new(
        f.Id, f.Name, f.Brand, f.Gtin, f.VerificationStatus.ToString(), f.Source.Provider,
        f.NutrientProfile.KcalPer100g, f.NutrientProfile.ProteinPer100g,
        f.NutrientProfile.FatPer100g, f.NutrientProfile.CarbPer100g,
        f.NutrientProfile.FiberPer100g, f.NutrientProfile.SugarPer100g, f.NutrientProfile.SodiumMgPer100g,
        f.Portions.Select(p => new PortionDto(p.Id, p.Name, p.Grams)).ToList());
}
