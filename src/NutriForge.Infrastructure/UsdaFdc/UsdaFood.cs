namespace NutriForge.Infrastructure.UsdaFdc;

/// <summary>A named portion from USDA FoodData Central (e.g. "1 cup" ⇒ 240 g).</summary>
public sealed record UsdaPortion(string Name, double Grams);

/// <summary>
/// One USDA FoodData Central food reduced to what the catalog stores: its FDC id (the dedup key), a
/// description, the per-100 g macro vector, and named portions. Provider-agnostic shape so the importer
/// can be fed from the CSV reader or a test fixture.
/// </summary>
public sealed record UsdaFood(
    string FdcId,
    string Description,
    double KcalPer100g,
    double ProteinPer100g,
    double FatPer100g,
    double CarbPer100g,
    IReadOnlyList<UsdaPortion> Portions);

/// <summary>Yields the USDA foods to import (a CSV directory, an API page, or a test fixture).</summary>
public interface IUsdaFoodSource
{
    Task<IReadOnlyList<UsdaFood>> LoadAsync(CancellationToken ct = default);
}
