using System.Linq;
using NutriForge.Infrastructure.UsdaFdc;

namespace NutriForge.Application.Tests;

/// <summary>
/// USDA FoodData Central import pipeline (#19): idempotent upsert by (provider, FDC id) — the
/// acceptance criterion (a re-run produces no duplicates) — and the relational CSV → UsdaFood parse.
/// </summary>
public sealed class UsdaImportTests
{
    private static readonly UsdaPortion[] BreastPortion = [new UsdaPortion("1 breast", 174)];

    private static List<UsdaFood> SampleFoods() =>
    [
        new("171688", "Chicken breast, raw", 120, 22.5, 2.6, 0, BreastPortion),
        new("169756", "White rice, cooked", 130, 2.7, 0.3, 28.2, []),
    ];

    [Fact]
    public async Task Import_upserts_idempotently_no_duplicates_on_rerun()
    {
        await using var db = TestDb.New(Guid.NewGuid());
        var importer = new UsdaFdcImporter(db);

        var first = await importer.ImportAsync(SampleFoods());
        Assert.Equal(2, first.Inserted);
        Assert.Equal(0, first.Updated);
        Assert.Equal(2, db.Foods.Count());

        // Re-run the SAME batch ⇒ all updated in place, still exactly two rows (the acceptance).
        var second = await importer.ImportAsync(SampleFoods());
        Assert.Equal(0, second.Inserted);
        Assert.Equal(2, second.Updated);
        Assert.Equal(2, db.Foods.Count());

        var chicken = db.Foods.Single(f => f.Source.ProviderId == "171688");
        Assert.Equal("usda", chicken.Source.Provider);
        Assert.Equal(120, chicken.NutrientProfile.KcalPer100g);
        Assert.Single(chicken.Portions); // portions set on insert, not duplicated on re-run
    }

    [Fact]
    public void Csv_parser_joins_foods_nutrients_and_portions()
    {
        const string food =
            "fdc_id,data_type,description\n" +
            "171688,sr_legacy,\"Chicken, broilers, raw\"\n" +
            "169756,sr_legacy,\"Rice, white, cooked\"";
        const string nutrient =
            "id,name,unit_name,nutrient_nbr\n" +
            "1008,Energy,KCAL,208\n1003,Protein,G,203\n1004,Total lipid (fat),G,204\n1005,Carbohydrate,G,205";
        const string foodNutrient =
            "id,fdc_id,nutrient_id,amount\n" +
            "1,171688,1008,120\n2,171688,1003,22.5\n3,171688,1004,2.6\n4,171688,1005,0\n" +
            "5,169756,1008,130\n6,169756,1003,2.7";
        const string portion =
            "id,fdc_id,amount,measure_unit_id,modifier,portion_description,gram_weight\n" +
            "1,171688,1,1000,,1 breast,174";
        const string measureUnit = "id,name\n1000,undetermined";

        var foods = UsdaFdcCsvParser.Parse(food, nutrient, foodNutrient, portion, measureUnit);

        Assert.Equal(2, foods.Count);
        var chicken = foods.Single(f => f.FdcId == "171688");
        Assert.Equal("Chicken, broilers, raw", chicken.Description); // quoted comma preserved
        Assert.Equal(120, chicken.KcalPer100g);
        Assert.Equal(22.5, chicken.ProteinPer100g);
        Assert.Equal(2.6, chicken.FatPer100g);
        Assert.Single(chicken.Portions);
        Assert.Equal(174, chicken.Portions[0].Grams);
    }

    [Fact]
    public void Csv_parser_skips_foods_without_macros()
    {
        const string food = "fdc_id,data_type,description\n999,sr_legacy,No nutrients food";
        const string nutrient = "id,name,unit_name,nutrient_nbr\n1008,Energy,KCAL,208";
        const string foodNutrient = "id,fdc_id,nutrient_id,amount\n"; // none for fdc 999

        Assert.Empty(UsdaFdcCsvParser.Parse(food, nutrient, foodNutrient));
    }
}
