using System.Text.Json.Serialization;

namespace NutriForge.Infrastructure.OpenFoodFacts;

// Subset of the Open Food Facts v2 product response we map. Nutriment keys are per-100g.
internal sealed class OffResponse
{
    [JsonPropertyName("status")] public int Status { get; set; }
    [JsonPropertyName("product")] public OffProduct? Product { get; set; }
}

internal sealed class OffProduct
{
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("product_name")] public string? ProductName { get; set; }
    [JsonPropertyName("brands")] public string? Brands { get; set; }
    [JsonPropertyName("serving_size")] public string? ServingSize { get; set; }
    [JsonPropertyName("nutriments")] public OffNutriments? Nutriments { get; set; }
}

internal sealed class OffNutriments
{
    [JsonPropertyName("energy-kcal_100g")] public double? EnergyKcal100g { get; set; }
    [JsonPropertyName("proteins_100g")] public double? Proteins100g { get; set; }
    [JsonPropertyName("fat_100g")] public double? Fat100g { get; set; }
    [JsonPropertyName("carbohydrates_100g")] public double? Carbohydrates100g { get; set; }
    [JsonPropertyName("fiber_100g")] public double? Fiber100g { get; set; }
    [JsonPropertyName("sugars_100g")] public double? Sugars100g { get; set; }
    [JsonPropertyName("sodium_100g")] public double? Sodium100g { get; set; } // grams per 100g
}
