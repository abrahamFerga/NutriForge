using NutriForge.Domain.Common;

namespace NutriForge.Domain.Catalog;

/// <summary>The provenance of a catalog food — which provider it came from and that provider's id.</summary>
public sealed record FoodSource(string Provider, string ProviderId)
{
    /// <summary>Provider for foods a user submitted by hand.</summary>
    public const string UserSubmittedProvider = "user";
}

/// <summary>
/// A food in the shared, public-read catalog. Carries a verification tier (never silently
/// blended) and a 1:1 nutrient profile + named portions. Mutations are audited.
/// </summary>
public sealed class Food : IAuditable, ITimestamped
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public string Name { get; set; } = string.Empty;
    public string? Brand { get; set; }

    /// <summary>GTIN / UPC barcode (indexed) for barcode logging; null for unbranded foods.</summary>
    public string? Gtin { get; set; }

    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.UserSubmitted;

    public FoodSource Source { get; set; } = new(FoodSource.UserSubmittedProvider, "");

    /// <summary>Canonical ingredient this food maps to (for recipes); resolved later, nullable at v1.</summary>
    public Guid? CanonicalIngredientId { get; set; }

    public NutrientProfile NutrientProfile { get; set; } = new();

    private readonly List<Portion> _portions = [];
    public IReadOnlyCollection<Portion> Portions => _portions;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Portion AddPortion(string name, double grams)
    {
        var portion = new Portion { FoodId = Id, Name = name, Grams = grams };
        _portions.Add(portion);
        return portion;
    }

    /// <summary>Macros for <paramref name="grams"/> grams of this food, from its per-100g profile.</summary>
    public Macros MacrosFor(double grams) => NutrientProfile.Per100g.Scale(grams / 100.0);
}
