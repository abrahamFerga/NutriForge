using NutriForge.Application.Food;

namespace NutriForge.Application.Abstractions;

/// <summary>
/// Caches food-search results (Redis in prod). Keyed on the normalized query; short TTL.
/// A miss returns null and the service falls through to the catalog.
/// </summary>
public interface IFoodSearchCache
{
    Task<IReadOnlyList<FoodSummary>?> GetAsync(string normalizedQuery, CancellationToken ct = default);

    Task SetAsync(string normalizedQuery, IReadOnlyList<FoodSummary> results, CancellationToken ct = default);
}
