using NutriForge.Domain.Catalog;

namespace NutriForge.Application.Abstractions;

/// <summary>
/// Fetches a branded food from Open Food Facts by barcode (GTIN/UPC). Outbound only, behind Polly;
/// returns an unsaved <see cref="Food"/> mapped to per-100g nutrients, or null when not found.
/// Open Food Facts data is ODbL-licensed — attribution is carried on the food's source.
/// </summary>
public interface IOpenFoodFactsClient
{
    Task<Domain.Catalog.Food?> FetchByBarcodeAsync(string gtin, CancellationToken ct = default);
}
