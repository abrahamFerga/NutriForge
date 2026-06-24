using Microsoft.EntityFrameworkCore;
using NutriForge.Domain.Catalog;
using NutriForge.Domain.Common;
using NutriForge.Domain.Recipes;

namespace NutriForge.Infrastructure.Persistence;

/// <summary>
/// Seeds a small catalog (foods + canonical ingredients + diet types + computed-nutrition recipes)
/// so search→log, barcode, recipes, shopping lists, and diet generation are all demoable in dev
/// without the full importers. Idempotent.
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

        // ---- Foods (per-100g) ----
        var chicken = Food("Chicken breast, skinless, raw", VerificationStatus.Authoritative, 120, 22.5, 2.6, 0, ("1 breast", 174));
        var rice = Food("White rice, cooked", VerificationStatus.Authoritative, 130, 2.7, 0.3, 28.2, ("1 cup", 158));
        var oats = Food("Rolled oats, dry", VerificationStatus.Authoritative, 379, 13.2, 6.5, 67.7, ("1/2 cup", 40));
        var banana = Food("Banana, raw", VerificationStatus.Authoritative, 89, 1.1, 0.3, 22.8, ("1 medium", 118));
        var oliveOil = Food("Olive oil", VerificationStatus.Authoritative, 884, 0, 100, 0, ("1 tbsp", 14));
        var almonds = Food("Almonds, raw", VerificationStatus.Community, 579, 21.2, 49.9, 21.6, ("1 oz", 28));
        var yogurt = Food("Greek yogurt, plain, nonfat", VerificationStatus.Community, 59, 10.2, 0.4, 3.6, ("1 container", 170));
        var tofu = Food("Tofu, firm", VerificationStatus.Authoritative, 144, 17.3, 8.7, 2.8, ("1/2 block", 126));
        var blackBeans = Food("Black beans, cooked", VerificationStatus.Authoritative, 132, 8.9, 0.5, 23.7, ("1/2 cup", 86));
        var spinach = Food("Spinach, raw", VerificationStatus.Authoritative, 23, 2.9, 0.4, 3.6, ("1 cup", 30));
        var peanutButter = Food("Peanut butter", VerificationStatus.Community, 588, 25, 50, 20, ("1 tbsp", 16));
        var egg = Food("Egg, whole, raw", VerificationStatus.Authoritative, 143, 12.6, 9.5, 0.7, ("1 large egg", 50));
        var milk = Food("Whole milk, 3.25%", VerificationStatus.Authoritative, 61, 3.2, 3.3, 4.8, ("1 cup", 244));
        db.Foods.AddRange(chicken, rice, oats, banana, oliveOil, almonds, yogurt, tofu, blackBeans, spinach, peanutButter, egg, milk);

        var cola = Food("Cola soft drink", VerificationStatus.Community, 42, 0, 0, 10.6, ("1 can (330 ml)", 330));
        cola.Gtin = "5449000000996";
        var proteinBar = Food("Protein bar, chocolate", VerificationStatus.Community, 350, 30, 12, 35, ("1 bar (60 g)", 60));
        proteinBar.Gtin = "0000000000017";
        db.Foods.AddRange(cola, proteinBar);

        // ---- Canonical ingredients (aisle + default food for nutrition) ----
        // YieldFactor (cooked = raw × factor) + whether the recipe states raw or cooked grams. The
        // basis mirrors the linked food: "raw chicken"/"dry oats" ⇒ recipe grams are raw; "cooked rice"/
        // "cooked beans" ⇒ recipe grams are cooked, so shopping divides the yield back out to buy raw.
        var iChicken = Ing("chicken breast", "Meat & Seafood", chicken, gramsPerCount: 174, aliases: ["chicken"], yield: 0.75, gramsAreRaw: true);
        var iRice = Ing("white rice", "Pantry", rice, yield: 2.8, gramsAreRaw: false);
        var iOats = Ing("rolled oats", "Pantry", oats, aliases: ["oats"], yield: 2.2, gramsAreRaw: true);
        var iBanana = Ing("banana", "Produce", banana, gramsPerCount: 118);
        var iOil = Ing("olive oil", "Pantry", oliveOil, density: 0.91);
        var iAlmonds = Ing("almonds", "Pantry", almonds);
        var iYogurt = Ing("greek yogurt", "Dairy & Eggs", yogurt, aliases: ["yogurt"]);
        var iTofu = Ing("tofu", "Produce", tofu);
        var iBeans = Ing("black beans", "Pantry", blackBeans, aliases: ["beans"], yield: 2.5, gramsAreRaw: false);
        var iSpinach = Ing("spinach", "Produce", spinach);
        var iPb = Ing("peanut butter", "Pantry", peanutButter);
        db.Ingredients.AddRange(iChicken, iRice, iOats, iBanana, iOil, iAlmonds, iYogurt, iTofu, iBeans, iSpinach, iPb);

        // ---- Diets as data ----
        db.DietTypes.AddRange(
            Diet("vegan", "Vegan", ["vegan"], ["chicken", "beef", "pork", "fish", "egg", "milk", "yogurt", "cheese", "honey", "gelatin"]),
            Diet("vegetarian", "Vegetarian", ["vegetarian"], ["chicken", "beef", "pork", "fish", "gelatin"]),
            Diet("high-protein", "High protein", ["high-protein"], []));

        // ---- Recipes (computed nutrition from resolved ingredients) ----
        db.Recipes.AddRange(
            Recipe("Tofu & black bean rice bowl", 2, 20, ["vegan", "vegetarian", "high-protein", "dinner"],
                RI(iTofu, tofu, 200), RI(iBeans, blackBeans, 150), RI(iRice, rice, 300), RI(iSpinach, spinach, 100), RI(iOil, oliveOil, 14)),
            Recipe("Oatmeal with banana & peanut butter", 1, 5, ["vegan", "vegetarian", "breakfast"],
                RI(iOats, oats, 80), RI(iBanana, banana, 118), RI(iPb, peanutButter, 32)),
            Recipe("Chicken & rice with spinach", 2, 25, ["high-protein", "dinner"],
                RI(iChicken, chicken, 300), RI(iRice, rice, 300), RI(iSpinach, spinach, 100), RI(iOil, oliveOil, 14)),
            Recipe("Greek yogurt with almonds & banana", 1, 5, ["vegetarian", "high-protein", "breakfast"],
                RI(iYogurt, yogurt, 170), RI(iAlmonds, almonds, 28), RI(iBanana, banana, 118)));

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static Domain.Catalog.Food Food(
        string name, VerificationStatus status, double kcal, double protein, double fat, double carb,
        params (string Name, double Grams)[] portions)
    {
        var food = new Domain.Catalog.Food
        {
            Name = name,
            VerificationStatus = status,
            Source = new FoodSource("seed", name),
            NutrientProfile = new NutrientProfile { KcalPer100g = kcal, ProteinPer100g = protein, FatPer100g = fat, CarbPer100g = carb },
        };
        foreach (var (pName, grams) in portions)
        {
            food.AddPortion(pName, grams);
        }

        return food;
    }

    private static Ingredient Ing(string name, string aisle, Domain.Catalog.Food food,
        double? gramsPerCount = null, double? density = null, string[]? aliases = null,
        double yield = 1.0, bool gramsAreRaw = true)
        => new()
        {
            CanonicalName = name,
            AisleCategory = aisle,
            DefaultFoodId = food.Id,
            GramsPerCount = gramsPerCount,
            DensityGPerMl = density,
            Aliases = [.. aliases ?? []],
            YieldFactor = yield,
            RecipeGramsAreRaw = gramsAreRaw,
        };

    private static DietType Diet(string slug, string name, List<string> tags, List<string> excluded)
        => new() { Slug = slug, Name = name, RequiredTags = tags, ExcludedKeywords = excluded };

    private static RecipeIngredient RI(Ingredient ingredient, Domain.Catalog.Food food, double grams)
    {
        var ri = new RecipeIngredient
        {
            RawText = $"{grams:0} g {ingredient.CanonicalName}",
            Quantity = grams,
            Unit = "g",
            IngredientId = ingredient.Id,
            IngredientName = ingredient.CanonicalName,
        };
        ri.SetContribution(grams, food.MacrosFor(grams).Rounded());
        return ri;
    }

    private static Recipe Recipe(string name, int servings, int minutes, List<string> tags, params RecipeIngredient[] ingredients)
    {
        var recipe = new Recipe { Name = name, Servings = servings, TotalMinutes = minutes, Tags = tags };
        foreach (var ri in ingredients)
        {
            recipe.AddIngredient(ri);
        }

        recipe.RecomputeNutrition();
        return recipe;
    }
}
