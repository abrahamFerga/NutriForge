using System.Globalization;
using System.Text;

namespace NutriForge.Infrastructure.UsdaFdc;

/// <summary>
/// Parses the USDA FoodData Central "Full Download" CSVs into <see cref="UsdaFood"/> records: joins
/// <c>food.csv</c> (descriptions) to <c>food_nutrient.csv</c> via <c>nutrient.csv</c> (mapping nutrient
/// ids → USDA nutrient numbers) to pull the four macros, and optionally <c>food_portion.csv</c> +
/// <c>measure_unit.csv</c> for named portions. Pure (takes CSV TEXT) so it's unit-testable without files.
/// </summary>
public static class UsdaFdcCsvParser
{
    // USDA nutrient numbers for the macro vector.
    private const string EnergyKcal = "208";
    private const string Protein = "203";
    private const string Fat = "204";       // Total lipid (fat)
    private const string Carb = "205";      // Carbohydrate, by difference

    public static IReadOnlyList<UsdaFood> Parse(
        string foodCsv, string nutrientCsv, string foodNutrientCsv,
        string? portionCsv = null, string? measureUnitCsv = null)
    {
        // nutrient id → nutrient number (203/204/205/208).
        var nutrientNumber = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in Rows(nutrientCsv))
        {
            if (row.TryGet("id", out var id) && row.TryGet("nutrient_nbr", out var nbr))
            {
                nutrientNumber[id] = nbr;
            }
        }

        // fdc_id → its macro amounts.
        var macros = new Dictionary<string, double[]>(StringComparer.Ordinal); // [kcal, protein, fat, carb]
        foreach (var row in Rows(foodNutrientCsv))
        {
            if (!row.TryGet("fdc_id", out var fdc) || !row.TryGet("nutrient_id", out var nid)
                || !nutrientNumber.TryGetValue(nid, out var nbr) || !row.TryGet("amount", out var amountStr)
                || !double.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
            {
                continue;
            }

            var slot = nbr switch { EnergyKcal => 0, Protein => 1, Fat => 2, Carb => 3, _ => -1 };
            if (slot < 0)
            {
                continue;
            }

            if (!macros.TryGetValue(fdc, out var vec))
            {
                vec = new double[4];
                macros[fdc] = vec;
            }

            vec[slot] = amount;
        }

        // measure_unit id → name, then fdc_id → portions.
        var portions = ParsePortions(portionCsv, measureUnitCsv);

        var foods = new List<UsdaFood>();
        foreach (var row in Rows(foodCsv))
        {
            if (!row.TryGet("fdc_id", out var fdc) || !row.TryGet("description", out var desc)
                || string.IsNullOrWhiteSpace(desc) || !macros.TryGetValue(fdc, out var vec))
            {
                continue; // skip foods with no macros (we can't compute anything useful from them)
            }

            foods.Add(new UsdaFood(
                fdc, desc.Trim(), vec[0], vec[1], vec[2], vec[3],
                portions.TryGetValue(fdc, out var ps) ? ps : []));
        }

        return foods;
    }

    private static Dictionary<string, List<UsdaPortion>> ParsePortions(string? portionCsv, string? measureUnitCsv)
    {
        var result = new Dictionary<string, List<UsdaPortion>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(portionCsv))
        {
            return result;
        }

        var unitName = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(measureUnitCsv))
        {
            foreach (var row in Rows(measureUnitCsv))
            {
                if (row.TryGet("id", out var id) && row.TryGet("name", out var name))
                {
                    unitName[id] = name;
                }
            }
        }

        foreach (var row in Rows(portionCsv))
        {
            if (!row.TryGet("fdc_id", out var fdc) || !row.TryGet("gram_weight", out var gw)
                || !double.TryParse(gw, NumberStyles.Any, CultureInfo.InvariantCulture, out var grams) || grams <= 0)
            {
                continue;
            }

            var amount = row.TryGet("amount", out var a) ? a : "";
            row.TryGet("modifier", out var modifier);
            row.TryGet("portion_description", out var pdesc);
            var unit = row.TryGet("measure_unit_id", out var muid) && unitName.TryGetValue(muid, out var un) && un != "undetermined" ? un : "";
            var name = string.Join(' ', new[] { amount, unit, modifier, pdesc }
                .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
            if (name.Length == 0)
            {
                name = "1 portion";
            }

            if (!result.TryGetValue(fdc, out var list))
            {
                list = [];
                result[fdc] = list;
            }

            list.Add(new UsdaPortion(name, grams));
        }

        return result;
    }

    // ---- minimal CSV reader (RFC4180-ish: quoted fields, doubled quotes, commas) ----

    private static IEnumerable<CsvRow> Rows(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            yield break;
        }

        string[]? header = null;
        foreach (var line in SplitLines(csv))
        {
            var fields = SplitFields(line);
            if (header is null)
            {
                header = fields;
                continue;
            }

            yield return new CsvRow(header, fields);
        }
    }

    private static IEnumerable<string> SplitLines(string csv)
    {
        // Split on newlines that are NOT inside quotes.
        var sb = new StringBuilder();
        var inQuotes = false;
        foreach (var c in csv)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                sb.Append(c);
            }
            else if ((c == '\n' || c == '\r') && !inQuotes)
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }

    private static string[] SplitFields(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        fields.Add(sb.ToString());
        return [.. fields];
    }

    private readonly struct CsvRow(string[] header, string[] fields)
    {
        public bool TryGet(string column, out string value)
        {
            for (var i = 0; i < header.Length; i++)
            {
                if (header[i].Equals(column, StringComparison.OrdinalIgnoreCase))
                {
                    value = i < fields.Length ? fields[i].Trim() : string.Empty;
                    return true;
                }
            }

            value = string.Empty;
            return false;
        }
    }
}
