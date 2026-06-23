using Microsoft.EntityFrameworkCore;
using NutriForge.Domain.Catalog;
using NutriForge.Domain.Common;

namespace NutriForge.Infrastructure.Persistence;

/// <summary>
/// Seeds a small catalog of authoritative foods so the search→log flow is demoable in dev
/// without running the full USDA importer (that pipeline is a separate backlog item). Idempotent.
/// </summary>
public static class DevDataSeeder
{
    public static async Task SeedAsync(NutriForgeDbContext db, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (await db.Foods.AnyAsync(ct).ConfigureAwait(false))
        {
            return;
        }

        db.Foods.AddRange(
            Food("Egg, whole, raw", null, VerificationStatus.Authoritative, 143, 12.6, 9.5, 0.7,
                ("1 medium egg", 44), ("1 large egg", 50)),
            Food("Chicken breast, skinless, raw", null, VerificationStatus.Authoritative, 120, 22.5, 2.6, 0,
                ("1 breast", 174), ("100 g", 100)),
            Food("White rice, cooked", null, VerificationStatus.Authoritative, 130, 2.7, 0.3, 28.2,
                ("1 cup", 158)),
            Food("Rolled oats, dry", null, VerificationStatus.Authoritative, 379, 13.2, 6.5, 67.7,
                ("1/2 cup", 40)),
            Food("Banana, raw", null, VerificationStatus.Authoritative, 89, 1.1, 0.3, 22.8,
                ("1 medium", 118)),
            Food("Whole milk, 3.25%", null, VerificationStatus.Authoritative, 61, 3.2, 3.3, 4.8,
                ("1 cup", 244)),
            Food("Olive oil", null, VerificationStatus.Authoritative, 884, 0, 100, 0,
                ("1 tbsp", 14)),
            Food("Almonds, raw", "Generic", VerificationStatus.Community, 579, 21.2, 49.9, 21.6,
                ("1 oz (23 nuts)", 28)),
            Food("Greek yogurt, plain, nonfat", "Generic", VerificationStatus.Community, 59, 10.2, 0.4, 3.6,
                ("1 container", 170)));

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static Domain.Catalog.Food Food(
        string name, string? brand, VerificationStatus status,
        double kcal, double protein, double fat, double carb,
        params (string Name, double Grams)[] portions)
    {
        var food = new Domain.Catalog.Food
        {
            Name = name,
            Brand = brand,
            VerificationStatus = status,
            Source = new FoodSource("seed", name),
            NutrientProfile = new NutrientProfile
            {
                KcalPer100g = kcal,
                ProteinPer100g = protein,
                FatPer100g = fat,
                CarbPer100g = carb,
            },
        };

        foreach (var (pName, grams) in portions)
        {
            food.AddPortion(pName, grams);
        }

        return food;
    }
}
