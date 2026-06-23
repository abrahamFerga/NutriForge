using Microsoft.EntityFrameworkCore;
using NutriForge.Domain.Catalog;
using NutriForge.Domain.Recipes;

namespace NutriForge.Application.Abstractions;

/// <summary>The shared, public-read food + recipe catalog (schema <c>catalog</c>) — not user-filtered.</summary>
public interface ICatalogDbContext
{
    DbSet<Domain.Catalog.Food> Foods { get; }
    DbSet<Portion> Portions { get; }
    DbSet<Recipe> Recipes { get; }
    DbSet<Ingredient> Ingredients { get; }
    DbSet<DietType> DietTypes { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
