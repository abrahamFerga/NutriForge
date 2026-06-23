using Microsoft.EntityFrameworkCore;
using NutriForge.Domain.Catalog;

namespace NutriForge.Application.Abstractions;

/// <summary>The shared, public-read food catalog (schema <c>catalog</c>) — not user-filtered.</summary>
public interface ICatalogDbContext
{
    DbSet<Domain.Catalog.Food> Foods { get; }
    DbSet<Portion> Portions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
