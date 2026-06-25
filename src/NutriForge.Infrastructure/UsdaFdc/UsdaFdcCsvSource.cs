namespace NutriForge.Infrastructure.UsdaFdc;

/// <summary>
/// Loads USDA foods from a directory of the FDC "Full Download" CSVs (food.csv, nutrient.csv,
/// food_nutrient.csv, and optionally food_portion.csv + measure_unit.csv). Targeted at the Foundation /
/// SR-Legacy subsets (tens of thousands of foods); the multi-GB Branded set would want streaming (a
/// follow-up). Nightly acquisition of the files is an ops step — the importer below is what we own.
/// </summary>
public sealed class UsdaFdcCsvSource(string directory) : IUsdaFoodSource
{
    public async Task<IReadOnlyList<UsdaFood>> LoadAsync(CancellationToken ct = default)
    {
        var food = await Required("food.csv", ct).ConfigureAwait(false);
        var nutrient = await Required("nutrient.csv", ct).ConfigureAwait(false);
        var foodNutrient = await Required("food_nutrient.csv", ct).ConfigureAwait(false);
        var portion = await Optional("food_portion.csv", ct).ConfigureAwait(false);
        var measureUnit = await Optional("measure_unit.csv", ct).ConfigureAwait(false);

        return UsdaFdcCsvParser.Parse(food, nutrient, foodNutrient, portion, measureUnit);
    }

    private Task<string> Required(string file, CancellationToken ct) =>
        File.ReadAllTextAsync(Path.Combine(directory, file), ct);

    private async Task<string?> Optional(string file, CancellationToken ct)
    {
        var path = Path.Combine(directory, file);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, ct).ConfigureAwait(false) : null;
    }
}
