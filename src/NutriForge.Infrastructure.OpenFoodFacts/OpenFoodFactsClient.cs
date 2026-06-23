using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NutriForge.Application.Abstractions;
using NutriForge.Domain.Catalog;
using NutriForge.Domain.Common;

namespace NutriForge.Infrastructure.OpenFoodFacts;

/// <summary>
/// Open Food Facts v2 client (outbound, behind the shared resilience handler). Maps a product to a
/// per-100g <see cref="Food"/> at the <see cref="VerificationStatus.Community"/> tier with ODbL
/// provenance. Faults degrade to null (the caller returns 404) rather than throwing.
/// </summary>
public sealed partial class OpenFoodFactsClient(HttpClient http, ILogger<OpenFoodFactsClient> logger)
    : IOpenFoodFactsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Food?> FetchByBarcodeAsync(string gtin, CancellationToken ct = default)
    {
        var code = (gtin ?? string.Empty).Trim();
        if (code.Length == 0)
        {
            return null;
        }

        try
        {
            var url = $"/api/v2/product/{Uri.EscapeDataString(code)}.json?fields=code,product_name,brands,serving_size,nutriments";
            var resp = await http.GetFromJsonAsync<OffResponse>(url, JsonOptions, ct).ConfigureAwait(false);
            return resp?.Status == 1 && resp.Product is not null ? Map(resp.Product, code) : null;
        }
#pragma warning disable CA1031 // outbound fetch is best-effort; never fail the request
        catch (Exception ex)
        {
            LogFetchFailed(logger, code, ex);
            return null;
        }
#pragma warning restore CA1031
    }

    internal static Food? Map(OffProduct p, string code)
    {
        var n = p.Nutriments;
        if (string.IsNullOrWhiteSpace(p.ProductName) || n?.EnergyKcal100g is null)
        {
            return null; // a usable food needs at least a name and calories
        }

        var food = new Food
        {
            Name = p.ProductName.Trim(),
            Brand = string.IsNullOrWhiteSpace(p.Brands) ? null : p.Brands.Split(',')[0].Trim(),
            Gtin = code,
            VerificationStatus = VerificationStatus.Community,
            Source = new FoodSource("openfoodfacts", code),
            NutrientProfile = new NutrientProfile
            {
                KcalPer100g = n.EnergyKcal100g.Value,
                ProteinPer100g = n.Proteins100g ?? 0,
                FatPer100g = n.Fat100g ?? 0,
                CarbPer100g = n.Carbohydrates100g ?? 0,
                FiberPer100g = n.Fiber100g,
                SugarPer100g = n.Sugars100g,
                SodiumMgPer100g = n.Sodium100g is { } sodium ? sodium * 1000 : null,
            },
        };

        if (ParseServingGrams(p.ServingSize) is { } grams && grams > 0)
        {
            food.AddPortion($"1 serving ({grams:0.#} g)", grams);
        }

        food.AddPortion("100 g", 100);
        return food;
    }

    private static double? ParseServingGrams(string? serving)
    {
        if (string.IsNullOrWhiteSpace(serving))
        {
            return null;
        }

        var match = ServingNumber().Match(serving);
        return match.Success && double.TryParse(match.Value, CultureInfo.InvariantCulture, out var grams)
            ? grams
            : null;
    }

    [GeneratedRegex(@"\d+(\.\d+)?", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex ServingNumber();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Open Food Facts fetch failed for {Gtin}")]
    private static partial void LogFetchFailed(ILogger logger, string gtin, Exception ex);
}
